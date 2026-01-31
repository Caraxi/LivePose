using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using LivePose.Capabilities.Posing;
using LivePose.Config;
using LivePose.Core;
using LivePose.Entities;
using LivePose.Game.Posing;
using LivePose.UI.Controls.Core;
using LivePose.UI.Controls.Editors;
using LivePose.UI.Controls.Stateless;
using LivePose.UI.Theming;
using OneOf.Types;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using LivePose.Capabilities.Actor;
using LivePose.Files;
using LivePose.IPC;
using LivePose.Resources;
using Lumina.Excel.Sheets;

namespace LivePose.UI.Windows.Specialized;

public class PosingOverlayToolbarWindow : Window
{
    private readonly PosingOverlayWindow _overlayWindow;
    private readonly EntityManager _entityManager;
    private readonly PosingTransformWindow _overlayTransformWindow;
    private readonly PosingService _posingService;
    private readonly ConfigurationService _configurationService;
    private readonly IClientState _clientState;
    private readonly PosingGraphicalWindow _graphicalWindow;
    private readonly ICondition _conditions;
    private readonly TimelineIdentification _timelineIdentification;
    private readonly IObjectTable _objectTable;
    private readonly IDataManager _dataManager;
    private readonly HeelsService _heelsService;
    private readonly SavedPoseWindow _savedPoseWindow;
    
    private readonly BoneSearchControl _boneSearchControl = new();

    private bool _pushedStyle = false;


    private const string _boneFilterPopupName = "livepose_bone_filter_popup";

    public PosingOverlayToolbarWindow(PosingOverlayWindow overlayWindow, EntityManager entityManager, PosingTransformWindow overlayTransformWindow, PosingService posingService, ConfigurationService configurationService, IClientState clientState, SettingsWindow settingsWindow, PosingGraphicalWindow graphicalWindow, ICondition conditions, TimelineIdentification timelineIdentification, IObjectTable objectTable, IDataManager dataManager, HeelsService heelsService, SavedPoseWindow savedPoseWindow) : base($"{LivePose.Name} OVERLAY###livepose_posing_overlay_toolbar_window", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
    {
        Namespace = "livepose_posing_overlay_toolbar_namespace";

        _overlayWindow = overlayWindow;
        _entityManager = entityManager;
        _overlayTransformWindow = overlayTransformWindow;
        _posingService = posingService;
        _configurationService = configurationService;
        _clientState = clientState;
        _graphicalWindow = graphicalWindow;
        _conditions = conditions;
        _timelineIdentification = timelineIdentification;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _heelsService = heelsService;
        _savedPoseWindow = savedPoseWindow;

        TitleBarButtons =
        [
            new()
            {
                Icon = FontAwesomeIcon.Cog,
                Click = _ => settingsWindow.Toggle(),
                ShowTooltip = () => ImGui.SetTooltip("Settings")
            }
        ];
        
        
        ShowCloseButton = false;
        
        
        
    }

    public override void PreOpenCheck()
    {
        IsOpen = _overlayWindow.IsOpen;
        

        if(UIManager.IsPosingGraphicalWindowOpen && _configurationService.Configuration.Posing.HideToolbarWhenAdvandedPosingOpen)
        {
            IsOpen = false;
        }

        base.PreOpenCheck();
    }

    public override bool DrawConditions()
    {
        if(_clientState.IsGPosing) return false;

        if(!_overlayWindow.IsOpen)
            return false;
        
        if(!_entityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing)) {
            return false;
        }

        if(posing.GameObject.ObjectIndex >= 2) return false;
        
        if(_objectTable.LocalPlayer == null) return false;
        if(_objectTable.LocalPlayer.StatusFlags.HasFlag(StatusFlags.InCombat)) return false;
        if(_conditions.AnyUnsafe()) return false;

        return base.DrawConditions();
    }

    public override void PreDraw()
    {
        base.PreDraw();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        _pushedStyle = true;
    }

    public override void Draw()
    {
        if(_pushedStyle)
        {
            _pushedStyle = false;
            ImGui.PopStyleVar(2);
        }

        if(!_entityManager.TryGetCapabilityFromSelectedEntity<PosingCapability>(out var posing))
            return;
        
        DrawButtons(posing);
        DrawBoneFilterPopup();
    }

    public override void PostDraw()
    {
        if(_pushedStyle)
        {
            _pushedStyle = false;
            ImGui.PopStyleVar(2);
        }

        base.PostDraw();
    }

    private void DrawButtons(PosingCapability posing)
    {

        float buttonSize = ImGui.GetTextLineHeight() * 3.2f;
        float buttonOperationSize = ImGui.GetTextLineHeight() * 2.4f;
        
        
        ImGui.PushStyleColor(ImGuiCol.Button, UIConstants.Transparent);

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            if(ImGui.Button($"{(_posingService.CoordinateMode == PosingCoordinateMode.Local ? FontAwesomeIcon.Globe.ToIconString() : FontAwesomeIcon.Atom.ToIconString())}###select_mode", new Vector2(buttonOperationSize)))
                _posingService.CoordinateMode = _posingService.CoordinateMode == PosingCoordinateMode.Local ? PosingCoordinateMode.World : PosingCoordinateMode.Local;
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip(_posingService.CoordinateMode == PosingCoordinateMode.Local ? "Switch to World" : "Switch to Local");

        ImGui.SameLine();

        using(ImRaii.PushColor(ImGuiCol.Text, _overlayTransformWindow.IsOpen ? UIConstants.ToggleButtonActive : UIConstants.ToggleButtonInactive))
        {
            using(ImRaii.PushFont(UiBuilder.IconFont))
            {
                if(ImGui.Button($"{FontAwesomeIcon.LocationCrosshairs.ToIconString()}###toggle_transforms_window", new Vector2(buttonOperationSize)))
                    _overlayTransformWindow.IsOpen = !_overlayTransformWindow.IsOpen;
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Toggle Transform Window");
        
        ImGui.SameLine();

        using(ImRaii.PushColor(ImGuiCol.Text, _graphicalWindow.IsOpen ? UIConstants.ToggleButtonActive : UIConstants.ToggleButtonInactive))
        {
            using(ImRaii.PushFont(UiBuilder.IconFont)) {
                if(ImGui.Button($"{FontAwesomeIcon.PersonThroughWindow.ToIconString()}###toggle_advanced_window", new Vector2(buttonOperationSize)))
                    _graphicalWindow.IsOpen = !_graphicalWindow.IsOpen;
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Toggle Advanced Pose Window");

        ImGui.SameLine();

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            if(ImGui.Button($"{FontAwesomeIcon.WindowClose.ToIconString()}###close_overlay", new Vector2(buttonOperationSize)))
                _overlayWindow.IsOpen = false;
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Close Overlay");

        ImGui.Separator();




        using(ImRaii.PushColor(ImGuiCol.Text, _posingService.Operation == PosingOperation.Translate ? UIConstants.ToggleButtonActive : UIConstants.ToggleButtonInactive))
        {
            using(ImRaii.PushFont(UiBuilder.IconFont))
            {
                if(ImGui.Button($"{FontAwesomeIcon.ArrowsUpDownLeftRight.ToIconString()}###select_position", new Vector2(buttonOperationSize)))
                    _posingService.Operation = PosingOperation.Translate;
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Position");

        ImGui.SameLine();


        using(ImRaii.PushColor(ImGuiCol.Text, _posingService.Operation == PosingOperation.Rotate ? UIConstants.ToggleButtonActive : UIConstants.ToggleButtonInactive))
        {
            using(ImRaii.PushFont(UiBuilder.IconFont))
            {
                if(ImGui.Button($"{FontAwesomeIcon.ArrowsSpin.ToIconString()}###select_rotate", new Vector2(buttonOperationSize)))
                    _posingService.Operation = PosingOperation.Rotate;
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Rotation");

        ImGui.SameLine();

        using(ImRaii.PushColor(ImGuiCol.Text, _posingService.Operation == PosingOperation.Scale ? UIConstants.ToggleButtonActive : UIConstants.ToggleButtonInactive))
        {
            using(ImRaii.PushFont(UiBuilder.IconFont))
            {
                if(ImGui.Button($"{FontAwesomeIcon.ExpandAlt.ToIconString()}###select_scale", new Vector2(buttonOperationSize)))
                    _posingService.Operation = PosingOperation.Scale;
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Scale");

        ImGui.SameLine();

        using(ImRaii.PushColor(ImGuiCol.Text, _posingService.Operation == PosingOperation.Universal ? UIConstants.ToggleButtonActive : UIConstants.ToggleButtonInactive))
        {
            using(ImRaii.PushFont(UiBuilder.IconFont))
            {
                if(ImGui.Button($"{FontAwesomeIcon.Cubes.ToIconString()}###select_universal", new Vector2(buttonOperationSize)))
                {
                    _posingService.Operation = PosingOperation.Universal;
                }
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Universal");

        ImGui.Separator();

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            if(ImGui.Button($"{FontAwesomeIcon.Bone.ToIconString()}###toggle_filter_window", new Vector2(buttonOperationSize)))
                ImGui.OpenPopup(_boneFilterPopupName);
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Bone Filter");

        
        ImGui.SameLine();
        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            if(ImGui.Button($"{FontAwesomeIcon.Search.ToIconString()}###bone_search", new Vector2(buttonOperationSize)))
                ImGui.OpenPopup("overlay_bone_search_popup");
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Bone Search");

        
        ImGui.SameLine();

        var bone = posing.Selected.Match(
          boneSelect => posing.SkeletonPosing.GetBone(boneSelect),
          _ => null,
          _ => null
       );

        var parentBone = bone?.Parent;

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            using(ImRaii.Disabled(parentBone == null))
            {
                if(ImGui.Button($"{FontAwesomeIcon.ArrowUp.ToIconString()}###select_parent", new Vector2(buttonOperationSize)))
                    posing.Selected = new BonePoseInfoId(parentBone!.Name, parentBone!.PartialId, PoseInfoSlot.Character);
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Select Parent");

        ImGui.SameLine();

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            using(ImRaii.Disabled(posing.Selected.Value is None))
            {
                if(ImGui.Button($"{FontAwesomeIcon.MinusSquare.ToIconString()}###clear_selected", new Vector2(buttonOperationSize)))
                    posing.ClearSelection();
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Clear Selection");
        
        
        // IK RED
        bool enabled = false;
        if(posing.Selected.Value is BonePoseInfoId boneId)
        {
            var bonePose = posing.SkeletonPosing.GetBonePose(boneId);
            var ik = bonePose.DefaultIK;
            enabled = ik.Enabled;
        }

        using(ImRaii.Disabled(!(bone?.EligibleForIK == true)))
        {
            using(ImRaii.PushColor(ImGuiCol.Button, ThemeManager.CurrentTheme.Accent.AccentColor, enabled))
            {
                if(ImGui.Button($"IK###bone_ik", new Vector2(buttonOperationSize, buttonSize)))
                    ImGui.OpenPopup("overlay_bone_ik");
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Inverse Kinematics");

        ImGui.SameLine();
        
        
        using(ImRaii.Disabled(!posing.SkeletonPosing.PoseInfo.HasIKStacks)) {
            if(ImGui.Button($"IK###clear_ik", new Vector2(buttonOperationSize, buttonSize))) {
                var pose = new PoseFile();
                posing.SkeletonPosing.ExportSkeletonPose(pose);
                foreach(var p in pose.Bones.Keys) {
                    var bBone = posing.SkeletonPosing.GetBone(p, PoseInfoSlot.Character);
                    if (bBone == null) continue;
                    var bonePoseInfo = posing.SkeletonPosing.GetBonePose(bBone);
                    
                    bonePoseInfo.ClearStacks();
                    bonePoseInfo.DefaultIK = BoneIKInfo.CalculateDefault(p);
                }
                
                posing.SkeletonPosing.ResetPose();
                posing.SkeletonPosing.ImportSkeletonPose(pose, new PoseImporterOptions(new BoneFilter(_posingService), TransformComponents.All));
            }
            
            var center = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
            var radius = MathF.Ceiling(ImGui.GetTextLineHeight() * 0.8f);
            var thickness = MathF.Ceiling(ImGui.GetTextLineHeight() * 0.1f);
            ImGui.GetWindowDrawList().AddCircle(center, radius, ImGui.GetColorU32(ImGuiCol.Text) & 0x80FFFFFF, 16, thickness);
            var offset = (radius - thickness) / MathF.Sqrt(2.0f);
            var lineStart = center + new Vector2(-offset, -offset);
            var lineEnd   = center + new Vector2(offset, offset);
            ImGui.GetWindowDrawList().AddLine(lineStart, lineEnd, 0x400000FF, thickness);
        }
        
        if(ImGui.IsItemHovered())
            using(ImRaii.Tooltip()) {
                ImGui.Text("Set Inverse Kinematics");
                ImGui.Separator();
                ImGui.TextDisabled("Disables Inverse Kinematics on all bones while\nkeeping them at their current position.");
            }
           
        ImGui.SameLine();
        PosingEditorCommon.DrawMirrorModeSelect(posing, new Vector2(buttonOperationSize, buttonSize));
        
        ImGui.SameLine();
        
        using(ImRaii.PushFont(UiBuilder.IconFont)) {
            if(ImGui.Button($"{FontAwesomeIcon.Repeat.ToIconString()}###mirrorPose", new Vector2(buttonOperationSize, buttonSize))) {
                posing.MirrorPose();
            }
        }
        if(ImGui.IsItemHovered()) {
            using(ImRaii.Tooltip()) {
                ImGui.Text("Mirror Pose");
                ImGui.TextColored(ImGuiColors.DalamudGrey, "Might look really bad for animated poses.\nUse at own risk.");
            }
        }
        
        if(_entityManager.TryGetCapabilityFromSelectedEntity<ActionTimelineCapability>(out var timelineCapability)) {

            using(ImRaii.PushColor(ImGuiCol.Text, timelineCapability.SpeedMultiplierOverride != null ? UIConstants.ToggleButtonActive : UIConstants.ToggleButtonInactive)) 
            using(ImRaii.PushFont(UiBuilder.IconFont)) {
                if(ImGui.Button($"{FontAwesomeIcon.TachometerAlt.ToIconString()}###animation_speed_button", new Vector2(buttonOperationSize))) {
                    ImGui.OpenPopup("animation_speed_editor");
                }
            }

            if(ImGui.IsItemHovered())
                ImGui.SetTooltip($"Animation Speed Control");
        }
        
        ImGui.SameLine();
        
        using (ImRaii.Disabled(posing.SkeletonPosing.ActiveMinion == 0))
        using(ImRaii.PushColor(ImGuiCol.Text, posing.SkeletonPosing.MinionLock != null ? UIConstants.ToggleButtonActive : UIConstants.ToggleButtonInactive)) 
        using(ImRaii.PushFont(UiBuilder.IconFont)) {
            if(ImGui.Button($"{FontAwesomeIcon.Paw.ToIconString()}###minion_control_button", new Vector2(buttonOperationSize))) {
                ImGui.OpenPopup("minion_control_popup");
            }
        }


        if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Minion Control");
        
                
        ImGui.SameLine();
        ImGui.Dummy(new Vector2(buttonOperationSize));
        
        ImGui.SameLine();
        
        using(ImRaii.PushFont(UiBuilder.IconFont)) {
            if(ImGui.Button($"{FontAwesomeIcon.FolderTree.ToIconString()}###open_saved_poses", new Vector2(buttonOperationSize)))
                _savedPoseWindow.Toggle();
        }
        
        if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Saved Poses");
        
        ImGui.Separator();

        ImGui.Dummy(new Vector2(buttonOperationSize));
        ImGui.SameLine();
        
        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            using(ImRaii.Disabled(!posing.CanUndo))
            {
                if(ImGui.Button($"{FontAwesomeIcon.Backward.ToIconString()}###undo_pose", new Vector2(buttonOperationSize)))
                {
                    posing.Undo();
                }
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Undo");

        ImGui.SameLine();

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            using(ImRaii.Disabled(!posing.CanRedo))
            {
                if(ImGui.Button($"{FontAwesomeIcon.Forward.ToIconString()}###redo_pose", new Vector2(buttonOperationSize)))
                {
                    posing.Redo();
                }
            }
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Redo");

        ImGui.Separator();

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            using(ImRaii.Disabled(!posing.HasOverride(posing.SkeletonPosing.FilterBodyBones)))
            {
                if(ImGui.Button($"{FontAwesomeIcon.Undo.ToIconString()}###reset_body_pose", new Vector2(buttonSize))) {
                    posing.Snapshot(false, reconcile: false);
                    posing.SkeletonPosing.PoseInfo.Clear(posing.SkeletonPosing.FilterBodyBones);
                    if(posing.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                        _heelsService.SetPlayerPoseTag();
                    }
                }
                
                ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2, ImGui.GetColorU32(ImGuiCol.Text), FontAwesomeIcon.ChildReaching.ToIconString());
                
            }
        }
        
        if(ImGui.IsItemHovered())
            using(ImRaii.Tooltip()) {
                ImGui.Text("Reset Body Pose");
                ImGui.Separator();
                ImGui.TextDisabled($"Will reset pose for:\n\t{_timelineIdentification.GetBodyPoseName(posing.SkeletonPosing.ActiveBodyTimelines)}");
            }
            
        ImGui.SameLine();

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            using(ImRaii.Disabled(!posing.HasOverride(posing.SkeletonPosing.FilterFaceBones)))
            {
                if(ImGui.Button($"{FontAwesomeIcon.Undo.ToIconString()}###reset_face_pose", new Vector2(buttonSize))) {
                    posing.Snapshot(false, reconcile: false);
                    posing.SkeletonPosing.PoseInfo.Clear(posing.SkeletonPosing.FilterFaceBones);
                    if(posing.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                        _heelsService.SetPlayerPoseTag();
                    }
                }
                
                ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2, ImGui.GetColorU32(ImGuiCol.Text), FontAwesomeIcon.Smile.ToIconString());
                
            }
        }
        
        if(ImGui.IsItemHovered())
            using(ImRaii.Tooltip()) {
                ImGui.Text("Reset Face Pose");
                ImGui.Separator();
                ImGui.TextDisabled($"Will reset pose for:\n\t{_timelineIdentification.GetExpressionName(posing.SkeletonPosing.ActiveFaceTimeline)}");
            }
        
        ImGui.SameLine();

        
        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            using(ImRaii.Disabled(!posing.HasOverride(b => b.Slot == PoseInfoSlot.Minion)))
            {
                if(ImGui.Button($"{FontAwesomeIcon.Undo.ToIconString()}###reset_minion_pose", new Vector2(buttonSize))) {
                    posing.Snapshot(false, reconcile: false);
                    posing.SkeletonPosing.PoseInfo.Clear(b => b.Slot == PoseInfoSlot.Minion);
                    if(posing.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                        _heelsService.SetPlayerPoseTag();
                    }
                }
                
                ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2, ImGui.GetColorU32(ImGuiCol.Text), FontAwesomeIcon.Paw.ToIconString());
                
            }
        }
        
        if(ImGui.IsItemHovered() && _dataManager.GetExcelSheet<Companion>().TryGetRow(posing.SkeletonPosing.ActiveMinion, out var activeMinion))
            using(ImRaii.Tooltip()) {
                ImGui.Text("Reset Minion Pose");
                ImGui.Separator();
                ImGui.TextDisabled($"Will reset pose for:\n\t{activeMinion.Singular}");
            }
        
        if(!_configurationService.Configuration.Posing.CursedMode) {
            ImGui.Separator();
            
            using(ImRaii.PushFont(UiBuilder.IconFont))
            {
                if(ImGui.Button($"{FontAwesomeIcon.Save.ToIconString()}###save_character_config", new Vector2(buttonSize))) {
                    posing.SkeletonPosing.SaveCharacterConfiguration();
                    if(posing.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                        _heelsService.SetPlayerPoseTag();
                    }
                }
                    
            }
            if(ImGui.IsItemHovered())
                using(ImRaii.Tooltip()) {
                    ImGui.Text("Save Character State");
                    ImGui.Separator();
                    var bCount = posing.SkeletonPosing.BodyPoses.Count;
                    var fCount = posing.SkeletonPosing.FacePoses.Count;
                    ImGui.Text($"Will save {bCount} body {(bCount > 1 ? "poses" : "pose")}\nand {fCount} face {(fCount > 1 ? "poses" : "pose")}.");
                    ImGui.Separator();
                    ImGui.TextDisabled("Unsaved character state will be lost when changing zones or logging out.\nSaved character state will be reloaded automatically.");
                }
            
            ImGui.SameLine();
            
            using (ImRaii.Disabled(!ImGui.GetIO().KeyShift))
            using(ImRaii.PushFont(UiBuilder.IconFont))
            {
                if(ImGui.Button($"{FontAwesomeIcon.IdBadge.ToIconString()}###load_character_config", new Vector2(buttonSize))) {
                    posing.SkeletonPosing.LoadCharacterConfiguration();
                    if(posing.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                        _heelsService.SetPlayerPoseTag();
                    }
                }
            }
            if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                using(ImRaii.Tooltip()) {
                    ImGui.Text("Reset Character to Saved State");
                    ImGui.Separator();
                    var bCount = posing.SkeletonPosing.BodyPoses.Count;
                    var fCount = posing.SkeletonPosing.FacePoses.Count;
                    ImGui.Text($"Will override {bCount} body {(bCount > 1 ? "poses" : "pose")}\nand {fCount} face {(fCount > 1 ? "poses" : "pose")}.");
                    if(!ImGui.GetIO().KeyShift) {
                        ImGui.Separator();
                        ImGui.TextColored(ImGuiColors.ParsedOrange, "Hold SHIFT to confirm.");
                    }
                }
            
            ImGui.SameLine();
            
            using(ImRaii.Disabled(!(ImGui.GetIO().KeyShift && ImGui.GetIO().KeyAlt)))
            using(ImRaii.PushFont(UiBuilder.IconFont))
            {


                if(ImGui.Button($"{FontAwesomeIcon.UndoAlt.ToIconString()}###clear_character_config", new Vector2(buttonSize))) {
                    posing.SkeletonPosing.BodyPoses.Clear();
                    posing.SkeletonPosing.FacePoses.Clear();
                    posing.SkeletonPosing.PoseInfo = new PoseInfo();
                    if(posing.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                        _heelsService.SetPlayerPoseTag();
                    }
                }
                
                
                ImGui.GetWindowDrawList().AddText(ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2, ImGui.GetColorU32((ImGui.GetIO().KeyShift && ImGui.GetIO().KeyAlt) ? ImGuiCol.Text : ImGuiCol.TextDisabled), FontAwesomeIcon.PersonHalfDress.ToIconString());
            }
            if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                using(ImRaii.Tooltip()) {
                    ImGui.Text("Clear All Poses");
                    ImGui.Separator();
                    var bCount = posing.SkeletonPosing.BodyPoses.Count;
                    var fCount = posing.SkeletonPosing.FacePoses.Count;
                    ImGui.Text($"Will remove {bCount} body {(bCount > 1 ? "poses" : "pose")}\nand {fCount} face {(fCount > 1 ? "poses" : "pose")}.");
                    if(!(ImGui.GetIO().KeyShift && ImGui.GetIO().KeyAlt)) {
                        ImGui.Separator();
                        ImGui.TextColored(ImGuiColors.ParsedOrange, "Hold ALT + SHIFT to confirm.");
                    }

                }

        }
        
        
        ImGui.Separator();


        ImGui.Dummy(new Vector2(buttonSize / 2));
        ImGui.SameLine();
        
        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            if(ImGui.Button($"{FontAwesomeIcon.FileImport.ToIconString()}###import_pose", new Vector2(buttonSize)))
                ImGui.OpenPopup("DrawImportPoseMenuPopup");
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Import Pose");

        FileUIHelpers.DrawImportPoseMenuPopup(posing, false);

        ImGui.SameLine();

        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            if(ImGui.Button($"{FontAwesomeIcon.FileExport.ToIconString()}###export_pose", new Vector2(buttonSize)))
                FileUIHelpers.ShowExportPoseModal(posing);
        }
        if(ImGui.IsItemHovered())
            ImGui.SetTooltip("Export Pose");

        
        /*
        ImGui.SameLine();

        using (ImRaii.Disabled())
        using(ImRaii.PushFont(UiBuilder.IconFont))
        {
            if(ImGui.Button($"{FontAwesomeIcon.Cog.ToIconString()}###import_options", new Vector2(buttonSize)))
                ImGui.OpenPopup("import_options_popup_pose_toolbar");
        }
        if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Import Options");
        */

        ImGui.PopStyleColor();

        using(var popup = ImRaii.Popup("import_options_popup_pose_toolbar"))
        {
            if(popup.Success)
            {
                PosingEditorCommon.DrawImportOptionEditor(_posingService.DefaultImporterOptions);
            }
        }

        using(var popup = ImRaii.Popup("overlay_bone_search_popup"))
        {
            if(popup.Success)
            {
                _boneSearchControl.Draw("overlay_bone_search", posing);
            }
        }

        using(var popup = ImRaii.Popup("overlay_bone_ik"))
        {
            if(popup.Success)
            {
                if(posing.Selected.Value is BonePoseInfoId id)
                {
                    var info = posing.SkeletonPosing.GetBonePose(id);
                    BoneIKEditor.Draw(info, posing);
                }
            }
        }

        using(var popup = ImRaii.Popup("animation_speed_editor")) {
            if(popup.Success && timelineCapability != null) {
                if(timelineCapability.SpeedMultiplierOverride is null) {
                    if(ImBrio.Button("Freeze", FontAwesomeIcon.Snowflake, new Vector2(250, 24) * ImGuiHelpers.GlobalScale)) {
                        timelineCapability.SetOverallSpeedOverride(0);
                        if(timelineCapability.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                            _heelsService.SetPlayerPoseTag();
                        }
                    }
                } else {
                    if(ImBrio.Button("Reset", FontAwesomeIcon.Undo, new Vector2(250, 24) * ImGuiHelpers.GlobalScale)) {
                        timelineCapability.ResetOverallSpeedOverride();
                    
                        if(timelineCapability.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                            _heelsService.SetPlayerPoseTag();
                        }
                    }
                }

                var v = (int) MathF.Round((timelineCapability.SpeedMultiplierOverride ?? 1f) * 100);
                ImGui.SetNextItemWidth(ImGui.GetItemRectSize().X);
                if(ImGui.SliderInt("##speed", ref v, -200, 200, "%d%%")) {
                    if(v == 100) {
                        timelineCapability.ResetOverallSpeedOverride();
                    } else {
                        timelineCapability.SetOverallSpeedOverride(v / 100f);
                    }
                    if(timelineCapability.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                        _heelsService.SetPlayerPoseTag();
                    }
                }
            }
        }

        using(var popup = ImRaii.Popup("minion_control_popup")) {
            if(popup.Success) {
                if(posing.SkeletonPosing.MinionLock == null) {
                    if(ImBrio.Button("Freeze Position", FontAwesomeIcon.Snowflake, new Vector2(250, 24) * ImGuiHelpers.GlobalScale)) {
                        posing.SkeletonPosing.LockMinionPosition();
                        if(posing.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                            _heelsService.SetPlayerPoseTag();
                        }
                    }
                } else {
                    if(ImBrio.Button("Unfreeze Position", FontAwesomeIcon.Snowflake, new Vector2(250, 24) * ImGuiHelpers.GlobalScale)) {
                        posing.SkeletonPosing.UnlockMinionPosition();
                        if(posing.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                            _heelsService.SetPlayerPoseTag();
                        }
                    }
                }

                if(timelineCapability != null) {
                    if(timelineCapability.MinionSpeedMultiplierOverride is null) {
                        if(ImBrio.Button("Freeze Animation", FontAwesomeIcon.Snowflake, new Vector2(250, 24) * ImGuiHelpers.GlobalScale)) {
                            timelineCapability.SetMinionSpeedOverride(0);
                            if(timelineCapability.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                                _heelsService.SetPlayerPoseTag();
                            }
                        }
                    } else {
                        if(ImBrio.Button("Reset Animation", FontAwesomeIcon.Undo, new Vector2(250, 24) * ImGuiHelpers.GlobalScale)) {
                            timelineCapability.ResetMinionSpeedOverride();
                    
                            if(timelineCapability.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                                _heelsService.SetPlayerPoseTag();
                            }
                        }
                    }

                    var v = (int) MathF.Round((timelineCapability.MinionSpeedMultiplierOverride ?? 1f) * 100);
                    ImGui.SetNextItemWidth(ImGui.GetItemRectSize().X);
                    if(ImGui.SliderInt("##speed", ref v, -200, 200, "%d%%")) {
                        if(v == 100) {
                            timelineCapability.ResetMinionSpeedOverride();
                        } else {
                            timelineCapability.SetMinionSpeedOverride(v / 100f);
                        }
                        if(timelineCapability.GameObject.ObjectIndex == 0 && _heelsService.IsAvailable) {
                            _heelsService.SetPlayerPoseTag();
                        }
                    }
                    
                    
                }
                
                
                
            }
        }
    }

    private void DrawBoneFilterPopup() {
        using var popup = ImRaii.Popup(_boneFilterPopupName);
        if(!popup.Success) return;

        if(!PosingEditorCommon.DrawBoneFilterEditor(_posingService.OverlayFilter)) return;
        
        _configurationService.Configuration.Posing.EnabledBoneCategories = _posingService.OverlayFilter.AllCategories.Where(_posingService.OverlayFilter.IsCategoryEnabled).Select(c => c.Id).ToArray();
        _configurationService.Save();
    }
}
