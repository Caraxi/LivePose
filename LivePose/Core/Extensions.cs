using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Plugin.Services;

namespace LivePose.Core;

public static class Extensions {
    public static bool AnyUnsafe(this ICondition condition) {
        return condition.Any(ConditionFlag.BetweenAreas, ConditionFlag.CreatingCharacter, ConditionFlag.LoggingOut,
            ConditionFlag.BetweenAreas51, ConditionFlag.Transformed, ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78,
            ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.BeingMoved, ConditionFlag.EditingPortrait, ConditionFlag.CarryingItem,
            ConditionFlag.CarryingObject, ConditionFlag.Disguised, ConditionFlag.Crafting, ConditionFlag.ExecutingCraftingAction,
            ConditionFlag.Gathering, ConditionFlag.ExecutingGatheringAction);
    }

    public static bool TryGetSelectedFilter(this FileDialogManager fileDialogManager, [NotNullWhen(true)] out string? selectedFilter) {
        selectedFilter = null;

        var dialog = fileDialogManager.GetType()
            .GetField("dialog", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(fileDialogManager);

        var selectedFilterStruct = dialog?.GetType()
            .GetField("selectedFilter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(dialog);

        selectedFilter = selectedFilterStruct?.GetType()
            .GetField("Filter", BindingFlags.Instance | BindingFlags.Public)?
            .GetValue(selectedFilterStruct) as string;
        
        return selectedFilter != null;
    }
        
}
