using System;
using Dalamud.Plugin.Services;
using LivePose.Capabilities.Actor;
using LivePose.Config;
using LivePose.Core;
using LivePose.Entities;
using LivePose.Entities.Actor;
using LivePose.Files;
using LivePose.Game.Posing;
using LivePose.Game.Posing.Skeletons;
using LivePose.Resources;
using LivePose.UI.Windows.Specialized;
using OneOf;
using OneOf.Types;
using System.Collections.Generic;
using System.Numerics;
using LivePose.IPC;

namespace LivePose.Capabilities.Posing;

public class PosingCapability : ActorCharacterCapability
{
    public PosingSelectionType Selected { get; set; } = new None();
    public PosingSelectionType Hover { get; set; } = new None();
    public PosingSelectionType LastHover { get; set; } = new None();

    public SkeletonPosingCapability SkeletonPosing => Entity.GetCapability<SkeletonPosingCapability>();

    public PosingService PosingService => _posingService;


    public bool IsEntitySelected;
    
    public bool HasOverride(Predicate<BonePoseInfoId>? predicate = null) {
        if(Entity.TryGetCapability<SkeletonPosingCapability>(out var skeletonPosing))
            if(skeletonPosing.PoseInfo.IsOverridden(predicate))
                return true;

        return false;
    }

    public bool CanUndo => _undoStack.Count is not 0 and not 1 || _groupedUndoService.CanUndo;
    public bool CanRedo => _redoStack.Count > 0 || _groupedUndoService.CanRedo;
    public bool HasIKApplied => SkeletonPosing.PoseInfo.HasIKStacks;

    private Stack<PoseStack> _undoStack = [];
    private Stack<PoseStack> _redoStack = [];

    public bool OverlayOpen
    {
        get => _overlayWindow.IsOpen;
        set
        {
            _overlayWindow.IsOpen = value;
        }
    }

    public bool TransformWindowOpen
    {
        get => _overlayTransformWindow.IsOpen;
        set => _overlayTransformWindow.IsOpen = value;
    }

    private readonly PosingOverlayWindow _overlayWindow;
    private readonly PosingService _posingService;
    private readonly ConfigurationService _configurationService;
    private readonly PosingTransformWindow _overlayTransformWindow;
    private readonly IFramework _framework;
    private readonly HistoryService _groupedUndoService;
    private readonly EntityManager _entityManager;

    public PosingCapability(
        ActorEntity parent,
        PosingOverlayWindow window,
        HistoryService groupedUndoService,
        PosingService posingService,
        EntityManager entityManager,
        ConfigurationService configurationService,
        PosingTransformWindow overlayTransformWindow,
        IFramework framework)
        : base(parent)
    {
        _overlayWindow = window;
        _posingService = posingService;
        _configurationService = configurationService;
        _overlayTransformWindow = overlayTransformWindow;
        _entityManager = entityManager;
        _framework = framework;
        _groupedUndoService = groupedUndoService;
    }
    
    public void ClearSelection() => Selected = PosingSelectionType.None;

    public void LoadResourcesPose(string resourcesPath, bool freezeOnLoad = false, bool asBody = false)
    {
        var option = _posingService.SceneImporterOptions;
        TransformComponents? tfc = null;
        if(asBody)
        {
            option = _posingService.BodyOptions;
            tfc = TransformComponents.Rotation;
        }

        ImportPose(JsonSerializer.Deserialize<PoseFile>(ResourceProvider.Instance.GetRawResourceString(resourcesPath)), option, freezeOnLoad: freezeOnLoad, asBody: asBody, transformComponents: tfc);
    }

    public void ImportPose(string path, PoseImporterOptions? options = null)
    {
        try
        {
            if(path.EndsWith(".cmp"))
            {
                ImportPose(ResourceProvider.Instance.GetFileDocument<CMToolPoseFile>(path), options);
                return;
            }

            ImportPose(ResourceProvider.Instance.GetFileDocument<PoseFile>(path), options);
        }
        catch
        {
            LivePose.Log.Warning("Invalid pose file.");
        }
    }

    public void ImportPose(OneOf<PoseFile, CMToolPoseFile, LivePoseFile> rawPoseFile, PoseImporterOptions? options = null, bool asExpression = false, bool asScene = false, bool asIPCpose = false, bool asBody = false,
        bool freezeOnLoad = false, bool asProp = false, TransformComponents? transformComponents = null)
    {
        if(Actor.TryGetCapability<ActionTimelineCapability>(out var actionTimeline))
        {
            LivePose.Log.Verbose($"Importing Pose... {asExpression} {asScene} {asIPCpose} {asBody} {freezeOnLoad}");

            actionTimeline.StopSpeedAndResetTimeline(() =>
            {
                ImportPose_Internal(rawPoseFile, options, reset: false, reconcile: false, asExpression: asExpression, asScene: asScene,
                    asIPCpose: asIPCpose, asBody: asBody, asProp: asProp, transformComponents: transformComponents, applyModelTransformOverride: false);

            }, true);
        }
        else
        {
            LivePose.Log.Warning($"Actor did not have ActionTimelineCapability while Importing a Pose... {asExpression} {asScene} {asIPCpose} {asBody} {freezeOnLoad}");
        }
    }

    // TODO change this boolean hell into flags after Scenes are added
    PoseFile? tempPose;
    internal void ImportPose_Internal(OneOf<PoseFile, CMToolPoseFile, LivePoseFile> rawPoseFile, PoseImporterOptions? options = null, bool generateSnapshot = true, bool reset = true, bool reconcile = true,
        bool asExpression = false, bool expressionPhase2 = false, bool asScene = false, bool asIPCpose = false, bool asBody = false, bool asProp = false,
        TransformComponents? transformComponents = null, bool? applyModelTransformOverride = null)
    {

        if(rawPoseFile.IsT2) {
            LivePose.Log.Warning("Live Pose Import not ready");
            return;
        }
        
        var poseFile = rawPoseFile.Match(
                poseFile => poseFile,
                cmToolPoseFile => cmToolPoseFile.Upgrade(),
                _ => throw new IndexOutOfRangeException()
            );

        if(poseFile.Bones.Count == 0 && poseFile.MainHand.Count == 0 && poseFile.OffHand.Count == 0)
        {
            LivePose.Log.Warning("Invalid pose file.");
            LivePose.Log.Verbose($"Invalid pose file. {reconcile} {reset} {generateSnapshot} {asExpression} {expressionPhase2} {asScene} {asIPCpose} {asBody}");
            return;
        }

        poseFile.SanitizeBoneNames();

        bool applyModelTransform = false;
        if(asExpression)
        {
            LivePose.Log.Info("Loading as Expression");

            options = _posingService.ExpressionOptions;
            tempPose = GeneratePoseFile();
        }
        else if(asBody)
        {
            options = _posingService.BodyOptions;
        }
        else if(asScene)
        {
            options = _posingService.SceneImporterOptions;

            applyModelTransform |= ConfigurationService.Instance.Configuration.Import.ApplyModelTransform;
        }
        else if(asIPCpose)
        {
            options = _posingService.DefaultIPCImporterOptions;
        }
        else
        {
            options ??= _posingService.DefaultImporterOptions;
        }

        if(asScene == false)
        {
            if(transformComponents.HasValue)
            {
                options.TransformComponents = transformComponents.Value;
            }
        }



        SkeletonPosing.ImportSkeletonPose(poseFile, options, expressionPhase2);

        if(generateSnapshot)
            _framework.RunOnTick(() => Snapshot(reset, reconcile, asExpression: asExpression), delayTicks: 4);
    }

    public PoseFile ExportPose()
    {
        return GeneratePoseFile();
    }
    public void ExportSavePose(string path)
    {
        var poseFile = ExportPose();
        ResourceProvider.Instance.SaveFileDocument(path, poseFile);
    }

    public void Snapshot(bool reset = true, bool reconcile = true, bool asExpression = false)
    {
        var undoStackSize = _configurationService.Configuration.Posing.UndoStackSize;
        if(undoStackSize <= 0)
        {
            _undoStack.Clear();
            _redoStack.Clear();
            return;
        }

        _redoStack.Clear();

        if(asExpression == true)
        {
            ImportPose_Internal(tempPose!, new PoseImporterOptions(new BoneFilter(_posingService), TransformComponents.All),
            generateSnapshot: true, expressionPhase2: true);

            return;
        }

        if(_undoStack.Count == 0)
            _undoStack.Push(new PoseStack(new PoseInfo(), Transform.Identity));

        _undoStack.Push(new PoseStack(SkeletonPosing.PoseInfo.Clone(), Transform.Identity));
        _undoStack = _undoStack.Trim(undoStackSize + 1);

        if(reconcile)
            Reconcile(reset);
    }

    public void Redo()
    {
        if(_entityManager.SelectedEntityIds.Count > 1)
        {
            _groupedUndoService.Redo();
            return;
        }

        if(_redoStack.TryPop(out var redoStack))
        {
            _undoStack.Push(redoStack);
            SkeletonPosing.PoseInfo = redoStack.Info.Clone();
        }
        
        if(GameObject.ObjectIndex == 0 && LivePose.TryGetService<HeelsService>(out var heelsService) && heelsService.IsAvailable) {
            heelsService.SetPlayerPoseTag();
        }
    }

    public void Undo()
    {
        if(_entityManager.SelectedEntityIds.Count > 1)
        {
            _groupedUndoService.Undo();
            return;
        }

        if(_undoStack.TryPop(out var undoStack))
            _redoStack.Push(undoStack);

        if(_undoStack.TryPeek(out var applicable))
        {
            SkeletonPosing.PoseInfo = applicable.Info.Clone();
        }

        if(GameObject.ObjectIndex == 0 && LivePose.TryGetService<HeelsService>(out var heelsService) && heelsService.IsAvailable) {
            heelsService.SetPlayerPoseTag();
        }
        
    }

    public void Reset(bool generateSnapshot = true, bool reset = true, bool clearHistStack = true)
    {
        if(Actor.IsProp == false)
            SkeletonPosing.ResetPose();

        if(clearHistStack)
            _redoStack.Clear();

        if(generateSnapshot)
            Snapshot(reset, reconcile: false);
    }

    private void Reconcile(bool reset = true, bool generateSnapshot = true)
    {
        _framework.RunOnTick(() =>
        {
            var all = new PoseImporterOptions(new BoneFilter(_posingService), TransformComponents.All);
            var poseFile = GeneratePoseFile();
            if(reset)
            {
                Reset(generateSnapshot, false);
            }
            ImportPose_Internal(poseFile, options: all, generateSnapshot: false);
        }, delayTicks: 2);
    }
    public PoseFile GeneratePoseFile()
    {
        var poseFile = new PoseFile();
        SkeletonPosing.ExportSkeletonPose(poseFile);
        return poseFile;
    }
    public BonePoseInfoId? IsSelectedBone()
    {
        Bone? realBone = null;
        return Selected.Match<BonePoseInfoId?>(
            bone =>
            {
                realBone = SkeletonPosing.GetBone(bone);
                if(realBone != null && realBone.Skeleton.IsValid)
                    return bone;
                return null;
            },
            _ => null,
            _ => null
        );
    }

    public static void FlipBone(Bone bone, BonePoseInfo poseInfo)
    {
        var newBoneTransform = bone.LastTransform;

        // Convert to Euler (like the Gizmo)
        var boneRotationEuler = bone.LastTransform.Rotation.ToEuler();
        boneRotationEuler.X = 180 - boneRotationEuler.X;
        boneRotationEuler.Y = -boneRotationEuler.Y;
        var newBoneRotation = boneRotationEuler.ToQuaternion();

        newBoneTransform.Rotation = newBoneRotation;

        poseInfo.Apply(newBoneTransform, bone.LastRawTransform, TransformComponents.All, TransformComponents.All, poseInfo.DefaultIK, poseInfo.MirrorMode, true);
    }

    public void FlipBoneModel()
    {
        BonePoseInfoId? selectedIsBone = IsSelectedBone();
        // Bone Flip
        if(selectedIsBone.HasValue)
        {
            // Get current bone rotation data
            var bone = SkeletonPosing.GetBone(selectedIsBone.Value);
            if(bone != null)
            {
                var poseInfo = SkeletonPosing.PoseInfo.GetPoseInfo(bone);
                FlipBone(bone, poseInfo);

                // record change for undo
                Snapshot(reset: false);
            }
        }
        else
        {
            // Model Flip (TODO: Implement)
        }
    }
    
    public void MirrorPose()
    {
        // Mirrors the entire pose, Left/right bone pairs (_l/_r) are swapped with mirrored transforms.
        // Center bones are mirrored in place. Weapons are swapped between hands.
        // Should work with all bones. Has some drifting if you use it like 70 times, but, who is going to do that?!?!?!


        var skeleton = SkeletonPosing.CharacterSkeleton;
        if(skeleton == null || skeleton.Bones.Count == 0)
            return;

        var currentPose = GeneratePoseFile();
        var mirroredPose = new PoseFile();

        var processedBones = new HashSet<string>(); // We need to track processed bones to avoid double-processing left/right pairs in some instances 

        foreach(var (boneName, transform) in currentPose.Bones)
        {
            if(processedBones.Contains(boneName))
                continue;

            var bone = skeleton.GetFirstVisibleBone(boneName);

            // Skip j_ex bones (PartialId 4) &
            // Skip partial root bones & skeleton root

            if(bone == null) continue;

            if(bone.Name.Contains("iv_shiri") || bone.Name.Contains("iv_kougan"))
            {
                continue;
            }

            if(bone.IsPartialRoot && !bone.IsSkeletonRoot)
            {
                mirroredPose.Bones[boneName] = transform;
                continue;
            }

            // Check for left or right bone pair
            string? oppositeBoneName = null;
            if(boneName.EndsWith("_l"))
            {
                oppositeBoneName = boneName[..^1] + "r";
                if(skeleton.GetFirstVisibleBone(oppositeBoneName) == null)
                    oppositeBoneName = null;
            }
            else if(boneName.EndsWith("_r"))
            {
                oppositeBoneName = boneName[..^1] + "l";
                if(skeleton.GetFirstVisibleBone(oppositeBoneName) == null)
                    oppositeBoneName = null;
            }

            if(oppositeBoneName != null && currentPose.Bones.TryGetValue(oppositeBoneName, out var oppositeTransform))
            {
                // Swap left to right pair with mirrored transforms
                mirroredPose.Bones[boneName] = MirrorBoneTransform(oppositeTransform);
                mirroredPose.Bones[oppositeBoneName] = MirrorBoneTransform(transform);

                processedBones.Add(boneName);
                processedBones.Add(oppositeBoneName);
            }
            else
            {
                // Center bone to mirror in place
                mirroredPose.Bones[boneName] = MirrorBoneTransform(transform);
                processedBones.Add(boneName);
            }
            

        }

        // Swap and mirror weapon bones between main hand and off hand, maybe we should something with props/ornament too, but I would have to do more clever things
        foreach(var (boneName, boneTransform) in currentPose.MainHand)
        {
            mirroredPose.OffHand[boneName] = MirrorBoneTransform(boneTransform);
        }
        foreach(var (boneName, boneTransform) in currentPose.OffHand)
        {
            mirroredPose.MainHand[boneName] = MirrorBoneTransform(boneTransform);
        }

        //This doesn't really work right, I need to do the same thing as the normal skeleton mirroring above to make this work properly, I can do that later
        //foreach(var (boneName, boneTransform) in currentPose.Ornament)
        //{
        //    mirroredPose.MainHand[boneName] = MirrorBoneTransform(boneTransform);
        //}

        // This one will need something clever
        //foreach(var (boneName, boneTransform) in currentPose.Prop)
        //{
        //    mirroredPose.MainHand[boneName] = MirrorBoneTransform(boneTransform);
        //}


        mirroredPose.ModelDifference = MirrorModelTransform(currentPose.ModelDifference);
        mirroredPose.ModelAbsoluteValues = MirrorModelTransform(currentPose.ModelAbsoluteValues);

        var options = new PoseImporterOptions(new BoneFilter(_posingService), TransformComponents.All);
        ImportPose_Internal(mirroredPose, options: options, generateSnapshot: true, reset: true, reconcile: true);
        
        
        if(GameObject.ObjectIndex == 0 && LivePose.TryGetService<HeelsService>(out var heelsService) && heelsService.IsAvailable) {
            heelsService.SetPlayerPoseTag();
        }
    }
    
    private static Transform MirrorBoneTransform(Transform transform)
    {
        var euler = transform.Rotation.ToEuler();
        euler.X = 180 - euler.X;
        euler.Y = -euler.Y;
        var mirroredRotation = euler.ToQuaternion();

        var mirroredPosition = new Vector3(-transform.Position.X, transform.Position.Y, transform.Position.Z);

        return new Transform
        {
            Position = mirroredPosition,
            Rotation = mirroredRotation,
            Scale = transform.Scale
        };
    }

    private static Transform MirrorModelTransform(Transform transform)
    {
        // Mirrors a model transform across the YZ plane.
        // Uses simple rotation mirroring (only negates Y rotation).

        var mirroredPosition = new Vector3(-transform.Position.X, transform.Position.Y, transform.Position.Z);

        var euler = transform.Rotation.ToEuler();
        euler.Y = -euler.Y;
        var mirroredRotation = euler.ToQuaternion();

        return new Transform
        {
            Position = mirroredPosition,
            Rotation = mirroredRotation,
            Scale = transform.Scale
        };
    }

    public record struct PoseStack(PoseInfo Info, Transform ModelTransform);
}

public enum ExpressionPhase
{
    None, One, Two, Three
}
