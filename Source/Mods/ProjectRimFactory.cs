using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Multiplayer.API;
using RimWorld;
using Verse;

namespace Multiplayer.Compat
{
    /// <summary>Project RimFactory Revived by the Project RimFactory team</summary>
    /// <see href="https://github.com/zymex22/Project-RimFactory-Revived"/>
    [MpCompatFor("spdskatr.projectrimfactory")]
    public class ProjectRimFactory
    {
        private const string Prefix = "ProjectRimFactory.";

        private static Type beltSplitterType;
        private static Type splitterLinkType;
        private static Type compPowerWorkType;
        private static Type smartHopperType;
        private static Type ioBaseType;
        private static Type outputSettingsType;
        private static Type slaughterSettingsType;
        private static Type paperclipDuplicatorType;
        private static Type colorCompType;

        private static MethodInfo itabSelThingGetter;
        private static MethodInfo splitterLinksGetter;
        private static MethodInfo splitterLinkActiveGetter;
        private static MethodInfo splitterLinkActiveSetter;
        private static MethodInfo splitterLinkCopyMethod;
        private static MethodInfo smartOutputSettingsGetter;
        private static MethodInfo ioOutputSettingsGetter;
        private static MethodInfo outputMinGetter;
        private static MethodInfo outputMinSetter;
        private static MethodInfo outputMaxGetter;
        private static MethodInfo outputMaxSetter;
        private static MethodInfo colorChangeMethod;

        private static FieldInfo splitterLinkPriorityField;
        private static FieldInfo splitterLinkFilterField;
        private static FieldInfo splitterLinkParentField;
        private static FieldInfo splitterParentFilterField;
        private static FieldInfo outputDialogSettingsField;
        private static FieldInfo outputDialogPostCloseField;
        private static FieldInfo atomicThingField;
        private static FieldInfo atomicSpeedField;
        private static FieldInfo paperclipBoundStorageField;
        private static FieldInfo paperclipDefField;
        private static FieldInfo droneSettingsCallerField;

        private static ISyncField outputUseMinSync;
        private static ISyncField outputUseMaxSync;
        private static ISyncField outputMinSync;
        private static ISyncField outputMaxSync;

        public ProjectRimFactory(ModContentPack mod)
        {
            LongEventHandler.ExecuteWhenFinished(LatePatch);
        }

        private static void LatePatch()
        {
            // PRF versions before the deterministic scheduler patch are unsafe even if
            // all of their UI commands are synchronized, so do not install partial support.
            if (AccessTools.TypeByName(Prefix + "AutoMachineTool.ScheduledCall") == null)
            {
                Log.Error("Multiplayer Compatibility :: Project RimFactory Revived is too old for multiplayer. " +
                          "Update to a version containing the deterministic ScheduledCall scheduler.");
                return;
            }

            itabSelThingGetter = AccessTools.PropertyGetter(typeof(ITab), "SelThing");

            RegisterConveyors();
            RegisterProductionAndPower();
            RegisterOutputSettings();
            RegisterAssemblers();
            RegisterDrones();
            RegisterStorage();
            RegisterIndustry();
        }

        private static Type ModType(string name) => AccessTools.TypeByName(Prefix + name);

        private static void Patch(MethodBase original, string prefix = null, string postfix = null)
        {
            if (original == null)
                throw new MissingMethodException("Project RimFactory compatibility target was not found");

            MpCompat.harmony.Patch(original,
                prefix == null ? null : new HarmonyMethod(typeof(ProjectRimFactory), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(ProjectRimFactory), postfix));
        }

        private static Thing SelectedThing(object tab)
        {
            return itabSelThingGetter?.Invoke(tab, Array.Empty<object>()) as Thing;
        }

        private static PropertyInfo Property(object instance, string name)
        {
            return instance == null ? null : AccessTools.Property(instance.GetType(), name);
        }

        private static FieldInfo Field(object instance, string name)
        {
            return instance == null ? null : AccessTools.Field(instance.GetType(), name);
        }

        private static bool TryGetBool(object instance, string name, out bool value)
        {
            var property = Property(instance, name);
            if (property?.CanRead == true && property.CanWrite)
            {
                value = (bool)property.GetValue(instance);
                return true;
            }

            value = default;
            return false;
        }

        private static ThingComp GetComp(Thing thing, Type compType)
        {
            return (thing as ThingWithComps)?.AllComps.FirstOrDefault(compType.IsInstanceOfType);
        }

        private static void RegisterConveyors()
        {
            var beltType = ModType("AutoMachineTool.Building_BeltConveyor");
            MP.RegisterSyncMethod(AccessTools.DeclaredMethod(beltType, "DropThing"));
            MP.RegisterSyncMethod(AccessTools.PropertySetter(beltType, "IsEndOfLine"));

            var outputCompType = ModType("Common.CompOutputAdjustable");
            MP.RegisterSyncMethod(AccessTools.PropertySetter(outputCompType, "Index"));

            var pullerType = ModType("AutoMachineTool.Building_ItemPuller");
            MpCompat.RegisterLambdaMethod(pullerType, "GetGizmos", 1, 3, 4);
            MP.RegisterSyncMethod(AccessTools.PropertySetter(pullerType, "TakeSingleItems"));

            beltSplitterType = ModType("AutoMachineTool.Building_BeltSplitter");
            splitterLinkType = ModType("AutoMachineTool.Building_BeltSplitter+OutputLink");
            splitterLinksGetter = AccessTools.PropertyGetter(beltSplitterType, "OutputLinks");
            splitterLinkActiveGetter = AccessTools.PropertyGetter(splitterLinkType, "Active");
            splitterLinkActiveSetter = AccessTools.PropertySetter(splitterLinkType, "Active");
            splitterLinkPriorityField = AccessTools.Field(splitterLinkType, "Priority");
            splitterLinkFilterField = AccessTools.Field(splitterLinkType, "filter");
            splitterLinkParentField = AccessTools.Field(splitterLinkType, "parent");
            splitterLinkCopyMethod = AccessTools.DeclaredMethod(splitterLinkType, "CopyAllowancesFrom");
            splitterParentFilterField = AccessTools.Field(ModType("Common.ThingFilterHelper"),
                "FilterConfigParentNoAnimalNoPlants");

            MP.RegisterSyncMethod(beltSplitterType, "AddOutgoingLink");
            MP.RegisterSyncMethod(AccessTools.PropertySetter(beltSplitterType, "IsEndOfLine"));
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncSplitterLink));
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncCopySplitterFilter));

            var splitterTabType = ModType("AutoMachineTool.ITab_ConveyorFilter");
            Patch(AccessTools.DeclaredMethod(splitterTabType, "FillTab"),
                nameof(PreSplitterTab), nameof(PostSplitterTab));
            Patch(AccessTools.DeclaredMethod(splitterLinkType, "DoThingFilterConfigWindow"),
                nameof(PreSplitterFilter), nameof(PostSplitterFilter));
            Patch(splitterLinkCopyMethod, nameof(PreCopySplitterFilter));

            PatchingUtilities.PatchPushPopRand(AccessTools.DeclaredConstructor(beltSplitterType));
        }

        private static IDictionary SplitterLinks(Thing splitter)
        {
            return splitterLinksGetter.Invoke(splitter, Array.Empty<object>()) as IDictionary;
        }

        private static object SplitterLink(Thing splitter, int direction)
        {
            var links = SplitterLinks(splitter);
            if (links == null)
                return null;

            var key = new Rot4(direction);
            return links.Contains(key) ? links[key] : null;
        }

        private static int SplitterDirection(object link)
        {
            var parent = splitterLinkParentField.GetValue(link) as Thing;
            var links = SplitterLinks(parent);
            if (links == null)
                return -1;

            foreach (DictionaryEntry entry in links)
                if (ReferenceEquals(entry.Value, link))
                    return ((Rot4)entry.Key).AsInt;

            return -1;
        }

        private sealed class SplitterLinkState
        {
            public bool Active;
            public int Priority;
        }

        private sealed class SplitterTabState
        {
            public Thing Splitter;
            public readonly Dictionary<int, SplitterLinkState> Links = new();
        }

        private static void PreSplitterTab(object __instance, ref SplitterTabState __state)
        {
            if (!MP.IsInMultiplayer)
                return;

            var splitter = SelectedThing(__instance);
            if (splitter == null || !beltSplitterType.IsInstanceOfType(splitter))
                return;

            __state = new SplitterTabState { Splitter = splitter };
            foreach (DictionaryEntry entry in SplitterLinks(splitter))
            {
                var link = entry.Value;
                __state.Links[((Rot4)entry.Key).AsInt] = new SplitterLinkState
                {
                    Active = (bool)splitterLinkActiveGetter.Invoke(link, Array.Empty<object>()),
                    Priority = Convert.ToInt32(splitterLinkPriorityField.GetValue(link))
                };
            }
        }

        private static void PostSplitterTab(SplitterTabState __state)
        {
            if (__state == null)
                return;

            foreach (var pair in __state.Links)
            {
                var link = SplitterLink(__state.Splitter, pair.Key);
                if (link == null)
                    continue;

                var active = (bool)splitterLinkActiveGetter.Invoke(link, Array.Empty<object>());
                var priority = Convert.ToInt32(splitterLinkPriorityField.GetValue(link));
                if (active == pair.Value.Active && priority == pair.Value.Priority)
                    continue;

                splitterLinkActiveSetter.Invoke(link, new object[] { pair.Value.Active });
                splitterLinkPriorityField.SetValue(link, Enum.ToObject(splitterLinkPriorityField.FieldType,
                    pair.Value.Priority));
                SyncSplitterLink(__state.Splitter, pair.Key, active, priority);
            }
        }

        private static void SyncSplitterLink(Thing splitter, int direction, bool active, int priority)
        {
            var link = SplitterLink(splitter, direction);
            if (link == null)
                return;

            splitterLinkActiveSetter.Invoke(link, new object[] { active });
            splitterLinkPriorityField.SetValue(link, Enum.ToObject(splitterLinkPriorityField.FieldType, priority));
        }

        private sealed record SplitterFilterContext(Thing Splitter, int Direction) : ThingFilterContext
        {
            public override ThingFilter Filter =>
                splitterLinkFilterField.GetValue(SplitterLink(Splitter, Direction)) as ThingFilter;

            public override ThingFilter ParentFilter =>
                splitterParentFilterField.GetValue(null) as ThingFilter;
        }

        private static void PreSplitterFilter(object __instance)
        {
            if (!MP.IsInMultiplayer)
                return;

            var parent = splitterLinkParentField.GetValue(__instance) as Thing;
            var direction = SplitterDirection(__instance);
            if (parent != null && direction >= 0)
                MP.SetThingFilterContext(new SplitterFilterContext(parent, direction));
        }

        private static void PostSplitterFilter()
        {
            if (MP.IsInMultiplayer)
                MP.SetThingFilterContext(null);
        }

        private static bool PreCopySplitterFilter(object __instance, ThingFilter __0)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;

            var parent = splitterLinkParentField.GetValue(__instance) as Thing;
            var direction = SplitterDirection(__instance);
            if (parent == null || direction < 0)
                return false;

            var source = parent.Map?.haulDestinationManager.AllGroups
                .FirstOrDefault(group => ReferenceEquals(group.Settings.filter, __0));
            if (source == null || source.CellsList.Count == 0)
                return false;

            SyncCopySplitterFilter(parent, direction, source.CellsList[0]);
            return false;
        }

        private static void SyncCopySplitterFilter(Thing splitter, int direction, IntVec3 sourceCell)
        {
            var link = SplitterLink(splitter, direction);
            var source = splitter?.Map?.haulDestinationManager.AllGroups
                .FirstOrDefault(group => group.CellsList.Contains(sourceCell));
            if (link != null && source != null)
                splitterLinkCopyMethod.Invoke(link, new object[] { source.Settings.filter });
        }

        private static void RegisterProductionAndPower()
        {
            compPowerWorkType = ModType("Common.CompPowerWorkSetting");

            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncProductionSettings));
            var productionTab = ModType("Common.ITab_ProductionSettings");
            Patch(AccessTools.DeclaredMethod(productionTab, "FillTab"),
                nameof(PreProductionTab), nameof(PostProductionTab));

            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncPowerSettings));
            var powerTab = ModType("Common.ITab_PowerSupply");
            Patch(AccessTools.DeclaredMethod(powerTab, "FillTab"),
                nameof(PrePowerTab), nameof(PostPowerTab));

            smartHopperType = ModType("SAL3.Things.Building_SmartHopper");
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncSmartHopperSettings));
            var hopperTab = ModType("SAL3.Things.ITab_SmartHopper");
            Patch(AccessTools.DeclaredMethod(hopperTab, "FillTab"),
                nameof(PreSmartHopperTab), nameof(PostSmartHopperTab));
        }

        [Flags]
        private enum ProductionMask
        {
            Output = 1,
            Obey = 2,
            Forbid = 4,
            Limit = 8,
            Count = 16,
            Stacks = 32,
            Slot = 64,
            Range = 128
        }

        private sealed class ProductionState
        {
            public Thing Target;
            public ProductionMask Mask;
            public bool Output;
            public bool Obey;
            public bool Forbid;
            public bool Limit;
            public int Count;
            public bool Stacks;
            public SlotGroup Slot;
            public string RangeType;
            public int RangeRotation;
        }

        private static ProductionState CaptureProduction(Thing target)
        {
            if (target == null)
                return null;

            var state = new ProductionState { Target = target };
            if (TryGetBool(target, "OutputToEntireStockpile", out state.Output)) state.Mask |= ProductionMask.Output;
            if (TryGetBool(target, "ObeysStorageFilters", out state.Obey)) state.Mask |= ProductionMask.Obey;
            if (TryGetBool(target, "ForbidOnPlacingDefault", out state.Forbid)) state.Mask |= ProductionMask.Forbid;

            var limit = Field(target, "productLimitation");
            var count = Field(target, "productLimitCount");
            var stacks = Field(target, "countStacks");
            var slot = Field(target, "targetSlotGroup");
            if (limit != null) { state.Mask |= ProductionMask.Limit; state.Limit = (bool)limit.GetValue(target); }
            if (count != null) { state.Mask |= ProductionMask.Count; state.Count = (int)count.GetValue(target); }
            if (stacks != null) { state.Mask |= ProductionMask.Stacks; state.Stacks = (bool)stacks.GetValue(target); }
            if (slot != null) { state.Mask |= ProductionMask.Slot; state.Slot = slot.GetValue(target) as SlotGroup; }

            var comp = GetComp(target, compPowerWorkType);
            if (comp != null)
            {
                state.Mask |= ProductionMask.Range;
                state.RangeType = Field(comp, "rangeCells")?.GetValue(comp)?.GetType().FullName;
                state.RangeRotation = ((Rot4)Field(comp, "RangeTypeRot").GetValue(comp)).AsInt;
            }
            return state;
        }

        private static bool SameProduction(ProductionState first, ProductionState second)
        {
            return first.Mask == second.Mask && first.Output == second.Output && first.Obey == second.Obey &&
                   first.Forbid == second.Forbid && first.Limit == second.Limit && first.Count == second.Count &&
                   first.Stacks == second.Stacks && ReferenceEquals(first.Slot, second.Slot) &&
                   first.RangeType == second.RangeType && first.RangeRotation == second.RangeRotation;
        }

        private static void ApplyProduction(ProductionState state, SlotGroup slot)
        {
            var target = state.Target;
            if (state.Mask.HasFlag(ProductionMask.Output)) Property(target, "OutputToEntireStockpile").SetValue(target, state.Output);
            if (state.Mask.HasFlag(ProductionMask.Obey)) Property(target, "ObeysStorageFilters").SetValue(target, state.Obey);
            if (state.Mask.HasFlag(ProductionMask.Forbid)) Property(target, "ForbidOnPlacingDefault").SetValue(target, state.Forbid);
            if (state.Mask.HasFlag(ProductionMask.Limit)) Field(target, "productLimitation").SetValue(target, state.Limit);
            if (state.Mask.HasFlag(ProductionMask.Count)) Field(target, "productLimitCount").SetValue(target, state.Count);
            if (state.Mask.HasFlag(ProductionMask.Stacks)) Field(target, "countStacks").SetValue(target, state.Stacks);
            if (state.Mask.HasFlag(ProductionMask.Slot)) Field(target, "targetSlotGroup").SetValue(target, slot);
            if (state.Mask.HasFlag(ProductionMask.Range))
            {
                var comp = GetComp(target, compPowerWorkType);
                if (comp != null)
                {
                    var rangeField = Field(comp, "rangeCells");
                    var rangeType = state.RangeType == null ? null : AccessTools.TypeByName(state.RangeType);
                    rangeField.SetValue(comp, rangeType == null ? null : Activator.CreateInstance(rangeType));
                    Field(comp, "RangeTypeRot").SetValue(comp, new Rot4(state.RangeRotation));
                }
            }
        }

        private static void PreProductionTab(object __instance, ref ProductionState __state)
        {
            if (MP.IsInMultiplayer)
                __state = CaptureProduction(SelectedThing(__instance));
        }

        private static void PostProductionTab(ProductionState __state)
        {
            if (__state == null)
                return;

            var changed = CaptureProduction(__state.Target);
            if (SameProduction(__state, changed))
                return;

            var hasSlot = changed.Slot != null && changed.Slot.CellsList.Count > 0;
            var slotCell = hasSlot ? changed.Slot.CellsList[0] : IntVec3.Invalid;
            ApplyProduction(__state, __state.Slot);
            SyncProductionSettings(changed.Target, (int)changed.Mask, changed.Output, changed.Obey, changed.Forbid,
                changed.Limit, changed.Count, changed.Stacks, hasSlot, slotCell,
                changed.RangeType, changed.RangeRotation);
        }

        private static void SyncProductionSettings(Thing target, int maskValue, bool output, bool obey, bool forbid,
            bool limit, int count, bool stacks, bool hasSlot, IntVec3 slotCell, string rangeType, int rangeRotation)
        {
            var state = new ProductionState
            {
                Target = target,
                Mask = (ProductionMask)maskValue,
                Output = output,
                Obey = obey,
                Forbid = forbid,
                Limit = limit,
                Count = count,
                Stacks = stacks,
                RangeType = rangeType,
                RangeRotation = rangeRotation
            };
            var slot = hasSlot
                ? target?.Map?.haulDestinationManager.AllGroups.FirstOrDefault(group => group.CellsList.Contains(slotCell))
                : null;
            ApplyProduction(state, slot);
        }

        private sealed class PowerState
        {
            public Thing Target;
            public float Speed;
            public float Range;
            public bool HasGlow;
            public bool Glow;
        }

        private static PowerState CapturePower(Thing target)
        {
            var comp = GetComp(target, compPowerWorkType);
            if (comp == null)
                return null;

            var state = new PowerState
            {
                Target = target,
                Speed = Convert.ToSingle(Property(comp, "SupplyPowerForSpeed").GetValue(comp)),
                Range = Convert.ToSingle(Property(comp, "SupplyPowerForRange").GetValue(comp))
            };
            state.HasGlow = (bool)Property(comp, "Glowable").GetValue(comp);
            if (state.HasGlow)
                state.Glow = (bool)Property(comp, "Glow").GetValue(comp);
            return state;
        }

        private static void ApplyPower(PowerState state)
        {
            var comp = GetComp(state.Target, compPowerWorkType);
            if (comp == null)
                return;
            Property(comp, "SupplyPowerForSpeed").SetValue(comp, state.Speed);
            Property(comp, "SupplyPowerForRange").SetValue(comp, state.Range);
            if (state.HasGlow)
                Property(comp, "Glow").SetValue(comp, state.Glow);
        }

        private static void PrePowerTab(object __instance, ref PowerState __state)
        {
            if (MP.IsInMultiplayer)
                __state = CapturePower(SelectedThing(__instance));
        }

        private static void PostPowerTab(PowerState __state)
        {
            if (__state == null)
                return;
            var changed = CapturePower(__state.Target);
            if (changed.Speed == __state.Speed && changed.Range == __state.Range &&
                (!changed.HasGlow || changed.Glow == __state.Glow))
                return;

            ApplyPower(__state);
            SyncPowerSettings(changed.Target, changed.Speed, changed.Range, changed.HasGlow, changed.Glow);
        }

        private static void SyncPowerSettings(Thing target, float speed, float range, bool hasGlow, bool glow)
        {
            ApplyPower(new PowerState { Target = target, Speed = speed, Range = range, HasGlow = hasGlow, Glow = glow });
        }

        private sealed class SmartHopperState
        {
            public Thing Target;
            public bool Ground;
            public bool Stockpile;
            public bool Storage;
            public bool Forbidden;
            public bool Belt;
        }

        private static SmartHopperState CaptureSmartHopper(Thing target)
        {
            if (target == null || !smartHopperType.IsInstanceOfType(target))
                return null;
            return new SmartHopperState
            {
                Target = target,
                Ground = (bool)Property(target, "AllowGroundPickup").GetValue(target),
                Stockpile = (bool)Property(target, "AllowStockpilePickup").GetValue(target),
                Storage = (bool)Property(target, "AllowStoragePickup").GetValue(target),
                Forbidden = (bool)Property(target, "AllowForbiddenPickup").GetValue(target),
                Belt = (bool)Property(target, "AllowBeltPickup").GetValue(target)
            };
        }

        private static void ApplySmartHopper(SmartHopperState state)
        {
            Property(state.Target, "AllowGroundPickup").SetValue(state.Target, state.Ground);
            Property(state.Target, "AllowStockpilePickup").SetValue(state.Target, state.Stockpile);
            Property(state.Target, "AllowStoragePickup").SetValue(state.Target, state.Storage);
            Property(state.Target, "AllowForbiddenPickup").SetValue(state.Target, state.Forbidden);
            Property(state.Target, "AllowBeltPickup").SetValue(state.Target, state.Belt);
        }

        private static void PreSmartHopperTab(object __instance, ref SmartHopperState __state)
        {
            if (MP.IsInMultiplayer)
                __state = CaptureSmartHopper(SelectedThing(__instance));
        }

        private static void PostSmartHopperTab(SmartHopperState __state)
        {
            if (__state == null)
                return;
            var changed = CaptureSmartHopper(__state.Target);
            if (changed.Ground == __state.Ground && changed.Stockpile == __state.Stockpile &&
                changed.Storage == __state.Storage && changed.Forbidden == __state.Forbidden && changed.Belt == __state.Belt)
                return;
            ApplySmartHopper(__state);
            SyncSmartHopperSettings(changed.Target, changed.Ground, changed.Stockpile, changed.Storage,
                changed.Forbidden, changed.Belt);
        }

        private static void SyncSmartHopperSettings(Thing target, bool ground, bool stockpile, bool storage,
            bool forbidden, bool belt)
        {
            ApplySmartHopper(new SmartHopperState
            {
                Target = target,
                Ground = ground,
                Stockpile = stockpile,
                Storage = storage,
                Forbidden = forbidden,
                Belt = belt
            });
        }

        private static void RegisterOutputSettings()
        {
            ioBaseType = ModType("Storage.Building_StorageUnitIOBase");
            outputSettingsType = ModType("Storage.OutputSettings");
            smartOutputSettingsGetter = AccessTools.PropertyGetter(smartHopperType, "OutputSettings");
            ioOutputSettingsGetter = AccessTools.PropertyGetter(ioBaseType, "OutputSettings");
            outputMinGetter = AccessTools.PropertyGetter(outputSettingsType, "Min");
            outputMinSetter = AccessTools.PropertySetter(outputSettingsType, "Min");
            outputMaxGetter = AccessTools.PropertyGetter(outputSettingsType, "Max");
            outputMaxSetter = AccessTools.PropertySetter(outputSettingsType, "Max");

            MP.RegisterSyncWorker<object>(SyncOutputSettings, outputSettingsType);
            outputUseMinSync = MP.RegisterSyncField(outputSettingsType, "UseMin");
            outputUseMaxSync = MP.RegisterSyncField(outputSettingsType, "UseMax");
            outputMinSync = MP.RegisterSyncField(outputSettingsType, "min");
            outputMaxSync = MP.RegisterSyncField(outputSettingsType, "max");
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncCopyOutputSettings));

            var dialogType = ModType("Storage.UI.Dialog_OutputMinMax");
            outputDialogSettingsField = AccessTools.Field(dialogType, "outputSettings");
            outputDialogPostCloseField = AccessTools.Field(dialogType, "postClose");
            Patch(AccessTools.DeclaredMethod(dialogType, "DoWindowContents"),
                nameof(PreOutputSettingsDialog), nameof(PostOutputSettingsDialog));
            Patch(AccessTools.DeclaredConstructor(dialogType, new[] { outputSettingsType, typeof(Action) }),
                postfix: nameof(PostOutputSettingsDialogConstructor));
        }

        private static object GetOutputSettings(Thing owner)
        {
            if (owner == null)
                return null;
            if (smartHopperType.IsInstanceOfType(owner))
                return smartOutputSettingsGetter.Invoke(owner, Array.Empty<object>());
            if (ioBaseType.IsInstanceOfType(owner))
                return ioOutputSettingsGetter.Invoke(owner, Array.Empty<object>());
            return null;
        }

        private static Thing FindOutputSettingsOwner(object settings)
        {
            if (settings == null || Current.Game == null)
                return null;
            foreach (var map in Find.Maps)
                foreach (var thing in map.listerThings.AllThings)
                {
                    if (!smartHopperType.IsInstanceOfType(thing) && !ioBaseType.IsInstanceOfType(thing))
                        continue;
                    // Do not call the lazy getter while searching: creating settings on
                    // unrelated buildings would itself mutate simulation state.
                    if (ReferenceEquals(Field(thing, "outputSettings")?.GetValue(thing), settings))
                        return thing;
                }
            return null;
        }

        private static void SyncOutputSettings(SyncWorker sync, ref object settings)
        {
            if (sync.isWriting)
                sync.Write(FindOutputSettingsOwner(settings));
            else
                settings = GetOutputSettings(sync.Read<Thing>());
        }

        private static void PreOutputSettingsDialog(ref bool __state)
        {
            if (!MP.IsInMultiplayer)
                return;
            var settings = outputDialogSettingsField.GetValue(null);
            if (settings == null)
                return;
            MP.WatchBegin();
            outputUseMinSync.Watch(settings);
            outputUseMaxSync.Watch(settings);
            outputMinSync.Watch(settings);
            outputMaxSync.Watch(settings);
            __state = true;
        }

        private static void PostOutputSettingsDialog(bool __state)
        {
            if (__state)
                MP.WatchEnd();
        }

        private static void PostOutputSettingsDialogConstructor(object __instance, object __0, Action __1)
        {
            if (!MP.IsInMultiplayer || __1 == null)
                return;
            var source = FindOutputSettingsOwner(__0);
            if (source == null)
                return;
            var targets = Find.Selector.SelectedObjects.OfType<Thing>()
                .Where(ioBaseType.IsInstanceOfType).Append(source).Distinct().ToArray();
            outputDialogPostCloseField.SetValue(__instance, (Action)(() => SyncCopyOutputSettings(source, targets)));
        }

        private static void SyncCopyOutputSettings(Thing source, Thing[] targets)
        {
            var sourceSettings = GetOutputSettings(source);
            if (sourceSettings == null || targets == null)
                return;
            var useMin = (bool)AccessTools.Field(outputSettingsType, "UseMin").GetValue(sourceSettings);
            var useMax = (bool)AccessTools.Field(outputSettingsType, "UseMax").GetValue(sourceSettings);
            var min = (int)outputMinGetter.Invoke(sourceSettings, Array.Empty<object>());
            var max = (int)outputMaxGetter.Invoke(sourceSettings, Array.Empty<object>());
            foreach (var target in targets)
            {
                var settings = GetOutputSettings(target);
                if (settings == null)
                    continue;
                AccessTools.Field(outputSettingsType, "UseMin").SetValue(settings, useMin);
                AccessTools.Field(outputSettingsType, "UseMax").SetValue(settings, useMax);
                outputMinSetter.Invoke(settings, new object[] { min });
                outputMaxSetter.Invoke(settings, new object[] { max });
            }
        }

        private static void RegisterAssemblers()
        {
            var assemblerType = ModType("SAL3.Things.Assemblers.Building_ProgrammableAssembler");
            MpCompat.RegisterLambdaMethod(assemblerType, "GetGizmos", 1);
            MP.RegisterSyncMethod(assemblerType, "MakeMatchingStockpileZone");

            var recipeHolderType = ModType("SAL3.Things.Building_RecipeHolder");
            MpCompat.RegisterLambdaMethod(recipeHolderType, "GetDebugOptions", 0).SetDebugOnly();
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncRecipeHolder));
            var recipeTabType = ModType("SAL3.Things.ITab_RecipeHolder");
            Patch(AccessTools.DeclaredMethod(recipeTabType, "FillTab"),
                nameof(PreRecipeTab), nameof(PostRecipeTab));
        }

        private sealed class RecipeState
        {
            public Thing Target;
            public RecipeDef[] Queue;
            public RecipeDef Current;
        }

        private static RecipeState CaptureRecipe(Thing target)
        {
            var queueProperty = Property(target, "QueuedRecipes");
            if (target == null || queueProperty == null)
                return null;
            var queue = ((IEnumerable)queueProperty.GetValue(target)).Cast<RecipeDef>().ToArray();
            return new RecipeState
            {
                Target = target,
                Queue = queue,
                Current = Field(target, "workingRecipe").GetValue(target) as RecipeDef
            };
        }

        private static void ApplyRecipe(RecipeState state)
        {
            var queue = Property(state.Target, "QueuedRecipes").GetValue(state.Target) as IList;
            queue.Clear();
            foreach (var recipe in state.Queue)
                queue.Add(recipe);
            Field(state.Target, "workingRecipe").SetValue(state.Target, state.Current);
        }

        private static void PreRecipeTab(object __instance, ref RecipeState __state)
        {
            if (MP.IsInMultiplayer)
                __state = CaptureRecipe(SelectedThing(__instance));
        }

        private static void PostRecipeTab(RecipeState __state)
        {
            if (__state == null)
                return;
            var changed = CaptureRecipe(__state.Target);
            if (__state.Queue.SequenceEqual(changed.Queue) && __state.Current == changed.Current)
                return;
            ApplyRecipe(__state);
            SyncRecipeHolder(changed.Target, changed.Queue, changed.Current);
        }

        private static void SyncRecipeHolder(Thing target, RecipeDef[] queue, RecipeDef current)
        {
            ApplyRecipe(new RecipeState { Target = target, Queue = queue ?? Array.Empty<RecipeDef>(), Current = current });
        }

        private static void RegisterDrones()
        {
            var droneStationType = ModType("Drones.Building_DroneStation");
            MpCompat.RegisterLambdaMethod(droneStationType, "GetGizmos", 0, 2, 3, 4);
            MpCompat.RegisterLambdaMethod(droneStationType, "GetGizmos", 5).SetDebugOnly();
            MP.RegisterSyncMethod(ModType("Drones.Building_DroneCultivator"), "MakeMatchingGrowZone");

            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncDroneWork));
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncDroneFuel));
            var droneSettingsType = ModType("Drones.ITab_DroneStation_Def");
            droneSettingsCallerField = AccessTools.Field(droneSettingsType, "caller");
            Patch(AccessTools.DeclaredMethod(droneSettingsType, "ITab_Settings_AppendContent"),
                nameof(PreDroneSettings), nameof(PostDroneSettings));

            var droneAreaType = ModType("Drones.DroneArea");
            PatchingUtilities.PatchPushPopRand(AccessTools.DeclaredConstructor(droneAreaType,
                new[] { typeof(AreaManager), typeof(string) }));
        }

        private sealed class DroneSettingsState
        {
            public Thing Target;
            public readonly Dictionary<WorkTypeDef, bool> Work = new();
            public CompRefuelable Refuelable;
            public float Fuel;
        }

        private static DroneSettingsState CaptureDroneSettings(object settingsObject)
        {
            var target = droneSettingsCallerField.GetValue(settingsObject) as Thing;
            if (target == null)
                return null;
            var state = new DroneSettingsState { Target = target };
            var work = Property(target, "GetWorkSettings").GetValue(target) as IDictionary;
            foreach (DictionaryEntry entry in work)
                state.Work[(WorkTypeDef)entry.Key] = (bool)entry.Value;
            state.Refuelable = Property(target, "CompRefuelable").GetValue(target) as CompRefuelable;
            if (state.Refuelable != null)
                state.Fuel = state.Refuelable.TargetFuelLevel;
            return state;
        }

        private static void PreDroneSettings(object __instance, ref DroneSettingsState __state)
        {
            if (MP.IsInMultiplayer)
                __state = CaptureDroneSettings(__instance);
        }

        private static void PostDroneSettings(object __instance, DroneSettingsState __state)
        {
            if (__state == null)
                return;
            var changed = CaptureDroneSettings(__instance);
            var work = Property(__state.Target, "GetWorkSettings").GetValue(__state.Target) as IDictionary;
            foreach (var pair in changed.Work)
            {
                if (__state.Work.TryGetValue(pair.Key, out var oldValue) && oldValue == pair.Value)
                    continue;
                if (__state.Work.TryGetValue(pair.Key, out oldValue)) work[pair.Key] = oldValue;
                else work.Remove(pair.Key);
                SyncDroneWork(__state.Target, pair.Key, pair.Value);
            }
            if (__state.Refuelable != null && changed.Fuel != __state.Fuel)
            {
                __state.Refuelable.TargetFuelLevel = __state.Fuel;
                SyncDroneFuel(__state.Target, changed.Fuel);
            }
        }

        private static void SyncDroneWork(Thing target, WorkTypeDef workType, bool enabled)
        {
            var work = Property(target, "GetWorkSettings").GetValue(target) as IDictionary;
            if (workType != null)
                work[workType] = enabled;
        }

        private static void SyncDroneFuel(Thing target, float fuel)
        {
            var refuelable = Property(target, "CompRefuelable").GetValue(target) as CompRefuelable;
            if (refuelable != null)
                refuelable.TargetFuelLevel = fuel;
        }

        private static void RegisterStorage()
        {
            var massStorageType = ModType("Storage.Building_MassStorageUnit");
            var coldStorageType = ModType("Storage.Building_ColdStorage");
            var ioPortType = ModType("Storage.Building_StorageUnitIOPort");
            MP.RegisterSyncMethod(AccessTools.PropertySetter(massStorageType, "RenamableLabel"));
            MP.RegisterSyncMethod(AccessTools.PropertySetter(coldStorageType, "RenamableLabel"));
            MP.RegisterSyncMethod(AccessTools.PropertySetter(ioBaseType, "RenamableLabel"));
            MpCompat.RegisterLambdaMethod(massStorageType, "GetGizmos", 1);
            MpCompat.RegisterLambdaMethod(ModType("Storage.Building_MassStorageUnitPowered"), "GetGizmos", 2);
            MpCompat.RegisterLambdaMethod(ModType("Storage.Building_ColdStoragePowered"), "GetGizmos", 2);
            MpCompat.RegisterLambdaMethod(ioBaseType, "GetGizmos", 12);
            MP.RegisterSyncMethod(AccessTools.PropertySetter(ioBaseType, "BoundStorageUnit"));
            MP.RegisterSyncMethod(AccessTools.PropertySetter(ioBaseType, "IOMode"));
            MP.RegisterSyncMethod(AccessTools.PropertySetter(ioPortType, "IOMode"));

            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncDropStoredItem));
            var itemsTabType = ModType("Storage.UI.ITab_Items");
            Patch(AccessTools.DeclaredMethod(itemsTabType, "DropThing"), nameof(PreDropStoredItem));
            MpCompat.RegisterLambdaMethod(itemsTabType, "ChoicesForThing", 0, 1, 2, 3, 4, 7);
        }

        private static bool PreDropStoredItem(object __instance, Thing __0)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;
            SyncDropStoredItem(SelectedThing(__instance), __0);
            return false;
        }

        private static void SyncDropStoredItem(Thing storage, Thing item)
        {
            if (storage == null || item == null)
                return;
            AccessTools.Method(storage.GetType(), "OutputItem")?.Invoke(storage, new object[] { item });
        }

        private static void RegisterIndustry()
        {
            RegisterAtomicReconstruction();
            RegisterPaperclips();
            RegisterColoredLights();
            RegisterSlaughterhouse();
        }

        private static void RegisterAtomicReconstruction()
        {
            var atomicType = ModType("Industry.Building_AtomicReconstructor");
            atomicThingField = AccessTools.Field(atomicType, "thingToGenerate");
            atomicSpeedField = AccessTools.Field(atomicType, "speedFactor");
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncAtomicReconstructor));
            var atomicTabType = ModType("Industry.UI.ITab_AtomicReconstruction");
            Patch(AccessTools.DeclaredMethod(atomicTabType, "FillTab"),
                nameof(PreAtomicTab), nameof(PostAtomicTab));
        }

        private sealed class AtomicState
        {
            public Thing Target;
            public ThingDef Def;
            public int Speed;
        }

        private static AtomicState CaptureAtomic(Thing target)
        {
            if (target == null || atomicThingField.DeclaringType?.IsInstanceOfType(target) != true)
                return null;
            return new AtomicState
            {
                Target = target,
                Def = atomicThingField.GetValue(target) as ThingDef,
                Speed = (int)atomicSpeedField.GetValue(target)
            };
        }

        private static void PreAtomicTab(object __instance, ref AtomicState __state)
        {
            if (MP.IsInMultiplayer)
                __state = CaptureAtomic(SelectedThing(__instance));
        }

        private static void PostAtomicTab(AtomicState __state)
        {
            if (__state == null)
                return;
            var changed = CaptureAtomic(__state.Target);
            if (changed.Def == __state.Def && changed.Speed == __state.Speed)
                return;
            // Restore the backing field directly: using the setter here would reset work
            // before the synchronized command executes on every client.
            atomicThingField.SetValue(__state.Target, __state.Def);
            atomicSpeedField.SetValue(__state.Target, __state.Speed);
            SyncAtomicReconstructor(changed.Target, changed.Def, changed.Speed);
        }

        private static void SyncAtomicReconstructor(Thing target, ThingDef def, int speed)
        {
            Property(target, "ThingToGenerate").SetValue(target, def);
            atomicSpeedField.SetValue(target, speed);
        }

        private static void RegisterPaperclips()
        {
            var paperclipPowerType = ModType("Industry.CompPaperclipPowerPlant");
            MpCompat.RegisterLambdaMethod(paperclipPowerType, "CompGetGizmosExtra", 2);

            paperclipDuplicatorType = ModType("Industry.Building_PaperclipDuplicator");
            paperclipBoundStorageField = AccessTools.Field(paperclipDuplicatorType, "BoundStorageUnit");
            paperclipDefField = AccessTools.Field(ModType("Common.PRFDefOf"), "Paperclip");
            MpCompat.RegisterLambdaMethod(paperclipDuplicatorType, "GetGizmos", 0).SetDebugOnly();
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncPaperclipStorage));
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncPaperclipTransaction));

            var tabType = ModType("Industry.UI.ITab_PaperclipDuplicator");
            Patch(AccessTools.DeclaredMethod(tabType, "FillTab"),
                nameof(PrePaperclipTab), nameof(PostPaperclipTab));
            Patch(AccessTools.DeclaredMethod(tabType, "HandleDepositWithdrawRequest"),
                nameof(PrePaperclipTransaction));
        }

        private sealed class PaperclipState
        {
            public Thing Target;
            public Thing Storage;
        }

        private static void PrePaperclipTab(object __instance, ref PaperclipState __state)
        {
            if (!MP.IsInMultiplayer)
                return;
            var target = SelectedThing(__instance);
            if (target != null && paperclipDuplicatorType.IsInstanceOfType(target))
                __state = new PaperclipState { Target = target, Storage = paperclipBoundStorageField.GetValue(target) as Thing };
        }

        private static void PostPaperclipTab(PaperclipState __state)
        {
            if (__state == null)
                return;
            var changed = paperclipBoundStorageField.GetValue(__state.Target) as Thing;
            if (changed == __state.Storage)
                return;
            paperclipBoundStorageField.SetValue(__state.Target, __state.Storage);
            SyncPaperclipStorage(__state.Target, changed);
        }

        private static void SyncPaperclipStorage(Thing duplicator, Thing storage)
        {
            if (duplicator != null && (storage == null || storage.Spawned))
                paperclipBoundStorageField.SetValue(duplicator, storage);
        }

        private static bool PrePaperclipTransaction(object __instance)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;
            var target = SelectedThing(__instance);
            var deposit = (bool)Field(__instance, "deposit").GetValue(__instance);
            var text = Field(__instance, "amountTextArea").GetValue(__instance) as string;
            if (target != null && int.TryParse(text, out var amount))
                SyncPaperclipTransaction(target, deposit, amount);
            return false;
        }

        private static void SyncPaperclipTransaction(Thing duplicator, bool deposit, int amount)
        {
            if (duplicator == null || amount < 0)
                return;
            var storage = paperclipBoundStorageField.GetValue(duplicator) as Thing;
            if (storage == null)
                return;
            var canReceive = Property(storage, "CanReceiveIO");
            if (canReceive == null || !(bool)canReceive.GetValue(storage))
                return;

            var paperclipDef = paperclipDefField?.GetValue(null) as ThingDef;
            var storedItems = (Property(storage, "StoredItems")?.GetValue(storage) as IEnumerable)?.Cast<Thing>().ToList();
            if (paperclipDef == null || storedItems == null)
                return;

            if (deposit)
            {
                var selected = new List<ThingCount>();
                var current = 0;
                foreach (var item in storedItems.Where(item => item.def == paperclipDef))
                {
                    var count = Math.Min(amount - current, item.stackCount);
                    selected.Add(new ThingCount(item, count));
                    current += count;
                    if (current == amount) break;
                }
                if (current != amount) return;
                AccessTools.Method(duplicator.GetType(), "DepositPaperclips").Invoke(duplicator, new object[] { amount });
                foreach (var thingCount in selected)
                    thingCount.Thing.SplitOff(thingCount.Count);
            }
            else
            {
                var paperclips = Convert.ToInt64(Property(duplicator, "PaperclipsActual").GetValue(duplicator));
                if (amount >= paperclips) return;
                var current = 0;
                while (current < amount)
                {
                    var count = Math.Min(amount - current, paperclipDef.stackLimit);
                    var paperclip = ThingMaker.MakeThing(paperclipDef);
                    paperclip.stackCount = count;
                    GenPlace.TryPlaceThing(paperclip, duplicator.Position, duplicator.Map, ThingPlaceMode.Direct);
                    AccessTools.Method(storage.GetType(), "Notify_ReceivedThing").Invoke(storage, new object[] { paperclip });
                    current += count;
                }
                AccessTools.Method(duplicator.GetType(), "WithdrawPaperclips").Invoke(duplicator, new object[] { amount });
            }
        }

        private static void RegisterColoredLights()
        {
            colorCompType = ModType("Misc.CompGlower_ColorPick");
            colorChangeMethod = AccessTools.DeclaredMethod(colorCompType, "ChangeColor");
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncColoredLights));
            Patch(colorChangeMethod, nameof(PreSingleColorChange));
            Patch(AccessTools.DeclaredMethod(colorCompType, "ChangeColorAllSelected"),
                nameof(PreSelectedColorChange));
        }

        private static bool PreSingleColorChange()
        {
            return !MP.IsInMultiplayer || !MP.InInterface || MP.IsExecutingSyncCommand;
        }

        private static bool PreSelectedColorChange(string __0)
        {
            if (!MP.IsInMultiplayer || MP.IsExecutingSyncCommand)
                return true;
            var selected = Find.Selector.SelectedObjects.OfType<Thing>()
                .Where(thing => GetComp(thing, colorCompType) != null).ToArray();
            SyncColoredLights(selected, __0);
            return false;
        }

        private static void SyncColoredLights(Thing[] selected, string colorKey)
        {
            if (selected == null)
                return;
            foreach (var thing in selected)
            {
                var comp = GetComp(thing, colorCompType);
                if (comp != null)
                    colorChangeMethod.Invoke(comp, new object[] { colorKey });
            }
        }

        private static void RegisterSlaughterhouse()
        {
            slaughterSettingsType = ModType("AutoMachineTool.SlaughterSettings");
            MP.RegisterSyncMethod(typeof(ProjectRimFactory), nameof(SyncSlaughterRow));
            var tabType = ModType("AutoMachineTool.ITab_Slaughterhouse");
            Patch(AccessTools.DeclaredMethod(tabType, "FillTab"),
                nameof(PreSlaughterTab), nameof(PostSlaughterTab));
        }

        private sealed class SlaughterRow
        {
            public bool Slaughter;
            public bool Bonds;
            public bool Pregnancy;
            public bool Trained;
            public int NoneYoung;
            public int NoneAdult;
            public int MaleAdult;
            public int MaleYoung;
            public int FemaleAdult;
            public int FemaleYoung;
        }

        private sealed class SlaughterState
        {
            public Thing Target;
            public IDictionary Settings;
            public readonly Dictionary<ThingDef, SlaughterRow> Rows = new();
        }

        private static SlaughterRow CaptureSlaughterRow(object row)
        {
            return new SlaughterRow
            {
                Slaughter = (bool)Field(row, "doSlaughter").GetValue(row),
                Bonds = (bool)Field(row, "hasBonds").GetValue(row),
                Pregnancy = (bool)Field(row, "pregnancy").GetValue(row),
                Trained = (bool)Field(row, "trained").GetValue(row),
                NoneYoung = (int)Field(row, "keepNoneYoungCount").GetValue(row),
                NoneAdult = (int)Field(row, "keepNoneAdultCount").GetValue(row),
                MaleAdult = (int)Field(row, "keepMaleAdultCount").GetValue(row),
                MaleYoung = (int)Field(row, "keepMaleYoungCount").GetValue(row),
                FemaleAdult = (int)Field(row, "keepFemaleAdultCount").GetValue(row),
                FemaleYoung = (int)Field(row, "keepFemaleYoungCount").GetValue(row)
            };
        }

        private static void ApplySlaughterRow(object row, SlaughterRow value)
        {
            Field(row, "doSlaughter").SetValue(row, value.Slaughter);
            Field(row, "hasBonds").SetValue(row, value.Bonds);
            Field(row, "pregnancy").SetValue(row, value.Pregnancy);
            Field(row, "trained").SetValue(row, value.Trained);
            Field(row, "keepNoneYoungCount").SetValue(row, value.NoneYoung);
            Field(row, "keepNoneAdultCount").SetValue(row, value.NoneAdult);
            Field(row, "keepMaleAdultCount").SetValue(row, value.MaleAdult);
            Field(row, "keepMaleYoungCount").SetValue(row, value.MaleYoung);
            Field(row, "keepFemaleAdultCount").SetValue(row, value.FemaleAdult);
            Field(row, "keepFemaleYoungCount").SetValue(row, value.FemaleYoung);
        }

        private static bool SameSlaughterRow(SlaughterRow a, SlaughterRow b)
        {
            return a.Slaughter == b.Slaughter && a.Bonds == b.Bonds && a.Pregnancy == b.Pregnancy &&
                   a.Trained == b.Trained && a.NoneYoung == b.NoneYoung && a.NoneAdult == b.NoneAdult &&
                   a.MaleAdult == b.MaleAdult && a.MaleYoung == b.MaleYoung &&
                   a.FemaleAdult == b.FemaleAdult && a.FemaleYoung == b.FemaleYoung;
        }

        private static void PreSlaughterTab(object __instance, ref SlaughterState __state)
        {
            if (!MP.IsInMultiplayer)
                return;
            var target = SelectedThing(__instance);
            var settings = Property(__instance, "Settings")?.GetValue(__instance) as IDictionary;
            if (target == null || settings == null)
                return;
            __state = new SlaughterState { Target = target, Settings = settings };
            foreach (DictionaryEntry entry in settings)
                __state.Rows[(ThingDef)entry.Key] = CaptureSlaughterRow(entry.Value);
        }

        private static void PostSlaughterTab(SlaughterState __state)
        {
            if (__state == null)
                return;
            var changes = new List<KeyValuePair<ThingDef, SlaughterRow>>();
            foreach (var def in __state.Settings.Keys.Cast<ThingDef>().ToList())
            {
                var currentObject = __state.Settings[def];
                var current = CaptureSlaughterRow(currentObject);
                if (__state.Rows.TryGetValue(def, out var old) && SameSlaughterRow(old, current))
                    continue;
                changes.Add(new KeyValuePair<ThingDef, SlaughterRow>(def, current));
                if (old != null) ApplySlaughterRow(currentObject, old);
                else __state.Settings.Remove(def);
            }
            foreach (var change in changes)
            {
                var row = change.Value;
                SyncSlaughterRow(__state.Target, change.Key, row.Slaughter, row.Bonds, row.Pregnancy, row.Trained,
                    row.NoneYoung, row.NoneAdult, row.MaleAdult, row.MaleYoung, row.FemaleAdult, row.FemaleYoung);
            }
        }

        private static void SyncSlaughterRow(Thing owner, ThingDef def, bool slaughter, bool bonds,
            bool pregnancy, bool trained, int noneYoung, int noneAdult, int maleAdult, int maleYoung,
            int femaleAdult, int femaleYoung)
        {
            var settings = Property(owner, "Settings")?.GetValue(owner) as IDictionary;
            if (settings == null || def == null)
                return;
            var row = settings.Contains(def) ? settings[def] : Activator.CreateInstance(slaughterSettingsType);
            Field(row, "def").SetValue(row, def);
            ApplySlaughterRow(row, new SlaughterRow
            {
                Slaughter = slaughter,
                Bonds = bonds,
                Pregnancy = pregnancy,
                Trained = trained,
                NoneYoung = noneYoung,
                NoneAdult = noneAdult,
                MaleAdult = maleAdult,
                MaleYoung = maleYoung,
                FemaleAdult = femaleAdult,
                FemaleYoung = femaleYoung
            });
            settings[def] = row;
        }
    }
}
