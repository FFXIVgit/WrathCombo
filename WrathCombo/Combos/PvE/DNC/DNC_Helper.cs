﻿#region

using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.JobGauge.Types;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;
using Options = WrathCombo.Combos.CustomComboPreset;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable CheckNamespace

#endregion

namespace WrathCombo.Combos.PvE;

internal partial class DNC
{
    /// <summary>
    ///     Logic to pick different openers.
    /// </summary>
    /// <returns>The chosen Opener.</returns>
    internal static WrathOpener Opener()
    {
        if (Config.DNC_ST_OpenerSelection == 0 && Opener1.LevelChecked)
            return Opener1;
        if (Config.DNC_ST_OpenerSelection == 1 && Opener2.LevelChecked)
            return Opener2;

        return WrathOpener.Dummy;
    }

    /// <summary>
    ///     Dancer Gauge data, just consolidated.
    /// </summary>
    private static DNCGauge Gauge => GetJobGauge<DNCGauge>();

    /// <summary>
    ///     Check if the rotation is in Auto-Rotation.
    /// </summary>
    /// <param name="singleTarget">
    ///     <c>true</c> if checking Single-Target combos.<br />
    ///     <c>false</c> if checking AoE combos.
    /// </param>
    /// <param name="simpleMode">
    ///     <c>true</c> if checking Simple Mode.<br />
    ///     <c>false</c> if checking Advanced Mode.
    /// </param>
    /// <returns>
    ///     Whether the Combo is in Auto-Mode and Auto-Rotation is enabled
    ///     (whether by user settings or another plugin).
    /// </returns>
    private static bool InAutoMode(bool singleTarget, bool simpleMode) =>
        P.IPC.GetAutoRotationState() && P.IPC.GetComboState(
            (singleTarget
                ? (simpleMode ? Options.DNC_ST_SimpleMode : Options.DNC_ST_AdvancedMode)
                : (simpleMode ? Options.DNC_AoE_SimpleMode : Options.DNC_AoE_AdvancedMode)
            ).ToString()
        )!.Values.Last();

    #region Openers

    internal static DNCOpenerMaxLevel1 Opener1 = new();

    internal class DNCOpenerMaxLevel1 : WrathOpener
    {
        public override int MinOpenerLevel => 100;

        public override int MaxOpenerLevel => 109;

        public override List<uint> OpenerActions { get; set; } =
        [
            StandardStep,
            Emboite,
            Emboite,
            StandardFinish2,
            TechnicalStep, //5
            Emboite,
            Emboite,
            Emboite,
            Emboite,
            TechnicalFinish4, //10
            Devilment,
            Tillana,
            Flourish,
            DanceOfTheDawn,
            FanDance4, //15
            LastDance,
            FanDance3,
            FinishingMove,
            StarfallDance,
            ReverseCascade, //20
            ReverseCascade,
            ReverseCascade,
        ];

        public override List<(int[] Steps, int HoldDelay)> PrepullDelays
        {
            get;
            set;
        } =
        [
            ([4], 12)
        ];

        public override List<(int[], uint, Func<bool>)> SubstitutionSteps
        {
            get;
            set;
        } =
        [
            ([2, 3, 6, 7, 8, 9], Entrechat, () => Gauge.NextStep == Entrechat),
            ([2, 3, 6, 7, 8, 9], Jete, () => Gauge.NextStep == Jete),
            ([2, 3, 6, 7, 8, 9], Pirouette, () => Gauge.NextStep == Pirouette),
            ([19], SaberDance, () => Gauge.Esprit >= 50),
            ([20, 21, 22], SaberDance, () => Gauge.Esprit > 80),
            ([20, 21, 22], StarfallDance,
                () => HasEffect(Buffs.FlourishingStarfall)),
            ([20, 21, 22], SaberDance, () => Gauge.Esprit >= 50),
            ([20, 21, 22], LastDance, () => HasEffect(Buffs.LastDanceReady)),
            ([20, 21, 22], Fountainfall, () =>
                HasEffect(Buffs.SilkenFlow) || HasEffect(Buffs.FlourishingFlow)),
        ];

        internal override UserData? ContentCheckConfig =>
            Config.DNC_ST_OpenerDifficulty;

        public override bool HasCooldowns()
        {
            if (!ActionReady(StandardStep))
                return false;

            if (!ActionReady(TechnicalStep))
                return false;

            if (!ActionReady(Devilment))
                return false;

            if (InCombat())
                return false;

            if (!CountdownActive)
                return false;

            // go at 15s, with some leeway
            if (CountdownRemaining is > 13.5f and < 16f)
                return false;

            return true;
        }
    }

    internal static DNCOpenerMaxLevel2 Opener2 = new();

    internal class DNCOpenerMaxLevel2 : WrathOpener
    {
        public override int MinOpenerLevel => 100;

        public override int MaxOpenerLevel => 109;

        public override List<uint> OpenerActions { get; set; } =
        [
            StandardStep,
            Emboite,
            Emboite,
            StandardFinish2,
            TechnicalStep, //5
            Emboite,
            Emboite,
            Emboite,
            Emboite,
            TechnicalFinish4, //10
            Devilment,
            Tillana,
            Flourish,
            DanceOfTheDawn,
            FanDance4, //15
            LastDance,
            FanDance3,
            StarfallDance,
            ReverseCascade,
            ReverseCascade, //20
            FinishingMove,
            ReverseCascade,
        ];

        public override List<(int[] Steps, int HoldDelay)> PrepullDelays
        {
            get;
            set;
        } =
        [
            ([4], 4)
        ];

        public override List<(int[], uint, Func<bool>)> SubstitutionSteps
        {
            get;
            set;
        } =
        [
            ([2, 3, 6, 7, 8, 9], Entrechat, () => Gauge.NextStep == Entrechat),
            ([2, 3, 6, 7, 8, 9], Jete, () => Gauge.NextStep == Jete),
            ([2, 3, 6, 7, 8, 9], Pirouette, () => Gauge.NextStep == Pirouette),
            ([21], SaberDance, () => Gauge.Esprit >= 50),
            ([19, 20, 22], SaberDance, () => Gauge.Esprit > 80),
            ([19, 20, 22], StarfallDance,
                () => HasEffect(Buffs.FlourishingStarfall)),
            ([19, 20, 22], SaberDance, () => Gauge.Esprit >= 50),
            ([19, 20, 22], LastDance, () => HasEffect(Buffs.LastDanceReady)),
            ([19, 20, 22], Fountainfall, () =>
                HasEffect(Buffs.SilkenFlow) || HasEffect(Buffs.FlourishingFlow)),
        ];

        internal override UserData? ContentCheckConfig =>
            Config.DNC_ST_OpenerDifficulty;

        public override bool HasCooldowns()
        {
            if (!ActionReady(StandardStep))
                return false;

            if (!ActionReady(TechnicalStep))
                return false;

            if (!ActionReady(Devilment))
                return false;

            if (InCombat())
                return false;

            if (!CountdownActive)
                return false;

            // go at 7s, with some leeway
            if (CountdownRemaining is > 5.5f and < 8f)
                return false;

            return true;
        }
    }

    #endregion
}
