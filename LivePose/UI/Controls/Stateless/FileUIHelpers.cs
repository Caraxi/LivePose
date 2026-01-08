using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using LivePose.Capabilities.Posing;
using LivePose.Config;
using LivePose.Core;
using LivePose.Files;
using LivePose.Library;
using LivePose.Library.Filters;
using LivePose.UI.Controls.Core;
using LivePose.UI.Controls.Editors;
using OneOf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using LivePose.IPC;
using LivePose.Resources;
using Newtonsoft.Json;

namespace LivePose.UI.Controls.Stateless;

public class FileUIHelpers
{
    

    static TransformComponents? transformComponents = null;
    public static void DrawImportPoseMenuPopup(PosingCapability capability, bool showImportOptions = true)
    {
        using var popup = ImRaii.Popup("DrawImportPoseMenuPopup");

        if(popup.Success)
        {
            var imIO = ImGui.GetIO();
            var _lastGlobalScale = imIO.FontGlobalScale;
            imIO.FontGlobalScale = 1f;

            using(ImRaii.PushColor(ImGuiCol.Button, UIConstants.Transparent))
            {
                var size = new Vector2(245, 44); //= ImGui.GetContentRegionAvail(); //ImGui.CalcTextSize("XXXX Freeze Actor on Import");

                size.Y = 44;

                
                var buttonSize = new Vector2((size.X - ImGui.GetStyle().ItemSpacing.X * 2 - 75f) / 3, 5f);

                transformComponents ??= capability.PosingService.DefaultImporterOptions.TransformComponents;
                
                static void ShowTransformTooltip(string type) {
                    using(ImRaii.Tooltip()) {
                        ImGui.Text($"Import {type}");
                        ImGui.TextDisabled("Does not effect Expression imports.");
                    }
                }
                
                if(ImBrio.ToggelFontIconButton("ImportPosition", FontAwesomeIcon.ArrowsUpDownLeftRight, buttonSize, transformComponents.Value.HasFlag(TransformComponents.Position), hoverAction: () => ShowTransformTooltip("Position")))
                {
                    if(transformComponents.Value.HasFlag(TransformComponents.Position))
                        transformComponents &= ~TransformComponents.Position;
                    else
                        transformComponents |= TransformComponents.Position;
                }
                ImGui.SameLine();
                if(ImBrio.ToggelFontIconButton("ImportRotation", FontAwesomeIcon.ArrowsSpin, buttonSize, transformComponents.Value.HasFlag(TransformComponents.Rotation), hoverAction: () => ShowTransformTooltip("Rotation")))
                {
                    if(transformComponents.Value.HasFlag(TransformComponents.Rotation))
                        transformComponents &= ~TransformComponents.Rotation;
                    else
                        transformComponents |= TransformComponents.Rotation;
                }
                ImGui.SameLine();
                if(ImBrio.ToggelFontIconButton("ImportScale", FontAwesomeIcon.ExpandAlt, buttonSize, transformComponents.Value.HasFlag(TransformComponents.Scale), hoverAction: () => ShowTransformTooltip("Scale")))
                {
                    if(transformComponents.Value.HasFlag(TransformComponents.Scale))
                        transformComponents &= ~TransformComponents.Scale;
                    else
                        transformComponents |= TransformComponents.Scale;
                }
                
                
                ImGui.Separator();

                if(ImGui.Button("Import Body", new(size.X, 35))) {
                    ShowImportPoseModal(capability, transformComponents: transformComponents);
                    
                }

                if(ImGui.Button("Import Expression", new(size.X, 35)))
                {
                    ShowImportPoseModal(capability, asExpression: true);
                }

                ImGui.Separator();

                if(ImGui.Button("Import A-Pose", new(size.X, 25)))
                {
                    capability.LoadResourcesPose("Data.BrioAPose.pose", freezeOnLoad: false, asBody: true);
                    ImGui.CloseCurrentPopup();
                }

                if(ImGui.Button("Import T-Pose", new(size.X, 25)))
                {
                    capability.LoadResourcesPose("Data.BrioTPose.pose", freezeOnLoad: false, asBody: true);
                    ImGui.CloseCurrentPopup();
                }
            }
            
            ImGui.GetIO().FontGlobalScale = _lastGlobalScale;
        }
    }

    public static void ShowImportPoseModal(PosingCapability capability, bool asExpression = false, TransformComponents? transformComponents = null)
    {
        TypeFilter filter = new("Poses", typeof(CMToolPoseFile), typeof(PoseFile), typeof(LivePoseFile));
        
        LibraryManager.GetWithFilePicker(filter, (r) =>
        {
            if(r is CMToolPoseFile cmPose)
            {
                ImportPose(capability, cmPose, transformComponents: transformComponents, expression: asExpression);
            }
            else if(r is PoseFile pose)
            {
                ImportPose(capability, pose, transformComponents: transformComponents, expression: asExpression);
            }
            else if(r is LivePoseFile livePose)
            {
                if(LivePose.TryGetService<IpcService>(out var ipcService)) {
                    var poseInfo = ipcService.DeserializePose(livePose.Data);
                    capability.SkeletonPosing.PoseInfo = poseInfo.Clone(asExpression ? capability.SkeletonPosing.FilterFaceBones : capability.SkeletonPosing.FilterBodyBones);
                    if(capability.GameObject.ObjectIndex == 0) {
                        if(LivePose.TryGetService<HeelsService>(out var service) && service.IsAvailable) {
                            service.SetPlayerPoseTag();
                        }
                    }
                    
                }
            }
        });
        
    }

    private static void ImportPose(PosingCapability capability, OneOf<PoseFile, CMToolPoseFile, LivePoseFile> rawPoseFile,
        TransformComponents? transformComponents = null, bool expression = false)
    {

        if(expression) {
            capability.ImportPose(rawPoseFile, options: null, asExpression: true, asBody: false, freezeOnLoad: false,
                transformComponents: null);
        } else {
            capability.ImportPose(rawPoseFile, options: null, asExpression: false, asBody: true, freezeOnLoad: false,
                transformComponents: transformComponents);
        }
        
        if(capability.GameObject.ObjectIndex == 0) {
            if(LivePose.TryGetService<HeelsService>(out var service) && service.IsAvailable) {
                service.SetPlayerPoseTag();
            }
        }
    }

    public static void ShowExportPoseModal(PosingCapability capability)
    {
        UIManager.Instance.FileDialogManager.SaveFileDialog("Export Pose###export_pose", "Pose Files (*.livepose | *.pose){.livepose,.pose}Live Pose File (*.livepose){.livepose}Pose File (*.pose){.pose}", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"), "",
                (success, path) =>
                {
                    if(success) {
                        if(!(path.EndsWith(".livepose") || path.EndsWith(".pose"))) {
                            if (UIManager.Instance.FileDialogManager.TryGetSelectedFilter(out var selectedFilter) && selectedFilter.Equals("Pose File (*.pose)", StringComparison.InvariantCultureIgnoreCase)) {
                                path += ".pose";
                            } else {
                                path += ".livepose";
                            }
                        }

                        var directory = Path.GetDirectoryName(path);
                        if(directory is not null)
                        {
                            ConfigurationService.Instance.Configuration.LastExportPath = directory;
                            ConfigurationService.Instance.Save();
                        }

                        if(path.EndsWith(".pose")) {
                            capability.ExportSavePose(path);
                        } else if(path.EndsWith(".livepose")) {

                            if(LivePose.TryGetService<IpcService>(out var ipcService)) {
                                var poseFile = new LivePoseFile() {
                                    Data = ipcService.SerializePose(capability.SkeletonPosing, capability.SkeletonPosing.PoseInfo)
                                };

                                var json = JsonConvert.SerializeObject(poseFile, Formatting.Indented);
                                
                                File.WriteAllText(path, json);
                                
                            }
                        }
                        
                        

                        
                    }
                }, ConfigurationService.Instance.Configuration.LastExportPath, true);
    }
}


