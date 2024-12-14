﻿#region

// ReSharper disable RedundantUsingDirective

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.EzEventManager;
using ECommons.Reflection;
using WrathCombo.Combos;
using WrathCombo.CustomComboNS.Functions;
using CancellationReasonEnum = WrathCombo.Services.IPC.CancellationReason;

// ReSharper disable UseSymbolAlias
// ReSharper disable UnusedMember.Global

#endregion

namespace WrathCombo.Services.IPC;

public class Lease(
    string internalPluginName,
    string pluginName,
    Action<CancellationReason, string>? callback)
{
    public Guid ID { get; } = Guid.NewGuid();
    public string InternalPluginName { get; } = internalPluginName;
    public string PluginName { get; } = pluginName;
    public Action<CancellationReason, string>? Callback { get; } = callback;

    // ReSharper disable once UnusedMember.Local
    private DateTime Created { get; } = DateTime.Now;
    internal DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    ///     A simple checksum of the configurations controlled by this registration.
    /// </summary>
    internal byte[] ConfigurationsHash
    {
        get
        {
            var allKeys = AutoRotationControlled.Keys
                .Select(k => k.ToString())
                .Concat(JobsControlled.Keys.Select(k => k.ToString()))
                .Concat(CombosControlled.Keys.Select(k => k.ToString()))
                .Concat(OptionsControlled.Keys.Select(k => k.ToString()))
                .ToArray();

            var concatenatedKeys = string.Join(",", allKeys);
            return SHA256.HashData(Encoding.UTF8.GetBytes(concatenatedKeys));
        }
    }

    /// <summary>
    ///     The number of sets leased by this registration currently.
    ///     Maximum is <c>40</c>.
    /// </summary>
    /// <seealso cref="Provider.RegisterForLease" />
    /// <seealso cref="Leasing.MaxLeaseConfigurations" />
    public int SetsLeased =>
        AutoRotationControlled.Count +
        JobsControlled.Count * 6 +
        CombosControlled.Count * 2 +
        OptionsControlled.Count;

    internal Dictionary<byte, bool> AutoRotationControlled { get; set; } = new();

    internal Dictionary<AutoRotationConfigOption, int> AutoRotationConfigsControlled
    {
        get;
        set;
    } = new();

    internal Dictionary<Job, bool> JobsControlled { get; set; } = new();

    internal Dictionary<CustomComboPreset, (bool enabled, bool autoMode)>
        CombosControlled { get; set; } =
        new();

    internal Dictionary<CustomComboPreset, bool> OptionsControlled { get; set; } =
        new();

    /// <summary>
    ///     Cancels the lease, invoking the <see cref="Callback" /> if one was
    ///     provided.
    /// </summary>
    /// <param name="cancellationReason">
    ///     The <see cref="CancellationReason" /> for cancelling the lease.
    /// </param>
    /// <param name="additionalInfo">
    ///     Any additional information to provide with the cancellation.
    /// </param>
    /// <remarks>
    ///     Usually called by <see cref="Leasing.RemoveRegistration" />,
    ///     which is often called by <see cref="Provider.ReleaseControl" />.
    /// </remarks>
    public void Cancel
        (CancellationReason cancellationReason, string additionalInfo = "")
    {
        Logging.Log(
            "Cancelling Lease for: "
            + PluginName
            + " (" + cancellationReason + ")" +
            (additionalInfo != ""
                ? "\n" + additionalInfo
                : "")
        );
        Callback?.Invoke(cancellationReason, additionalInfo);
    }
}

public partial class Leasing
{
    /// <summary>
    ///     The number of sets allowed per lease.
    /// </summary>
    /// <seealso cref="Provider.RegisterForLease" />
    /// <seealso cref="CheckLeaseConfigurationsAvailable" />
    /// <seealso cref="Lease.SetsLeased" />
    internal const int MaxLeaseConfigurations = 40;

    /// <summary>
    ///     Active leases.
    /// </summary>
    internal Dictionary<Guid, Lease> Registrations = new();

    #region Cache Bust dates

    /// <summary>
    ///     When the Auto-Rotation state was last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? AutoRotationStateUpdated;

    /// <summary>
    ///     When the Auto-Rotation configurations were last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? AutoRotationConfigsUpdated;

    /// <summary>
    ///     When Jobs-controlled were last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? JobsUpdated;

    /// <summary>
    ///     When Combos-controlled were last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? CombosUpdated;

    /// <summary>
    ///     When Options-controlled were last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? OptionsUpdated;

    #endregion

    #region Normal IPC Flow

    /// <summary>
    ///     Creates a new <see cref="Lease" /> and saves it to
    ///     <see cref="Registrations" />, ensuring the lease ID is unique.
    /// </summary>
    /// <param name="internalPluginName">
    ///     The internal name of the registering plugin.
    /// </param>
    /// <param name="pluginName">The name of the registering plugin.</param>
    /// <param name="callback">The cancellation callback for that plugin.</param>
    /// <returns>
    ///     The lease ID to be used by the plugin in subsequent calls.<br />
    ///     Or <c>null</c> if the plugin is blacklisted.
    /// </returns>
    /// <seealso cref="Provider.RegisterForLease" />
    internal Guid? CreateRegistration
    (string internalPluginName, string pluginName,
        Action<CancellationReason, string>? callback)
    {
        // Bail if the plugin is temporarily blacklisted
        if (CheckBlacklist(internalPluginName))
            return null;

        // Make sure the lease ID is unique
        // (unnecessary, but could save a big headache)
        Lease lease;
        do
        {
            // Create a new lease
            lease = new Lease(internalPluginName, pluginName, callback);
        } while (CheckLeaseExists(lease.ID) || CheckBlacklist(lease.ID));

        // Save the lease
        Registrations.Add(lease.ID, lease);

        Logging.Log($"{pluginName}: Created Lease");

        // Provide the lease ID to the plugin
        return lease.ID;
    }

    /// <summary>
    ///     Checks if Auto-Rotation's state is controlled by a lease.
    /// </summary>
    /// <returns>
    ///     The state Auto-Rotation is controlled to, or <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.GetAutoRotationState" />
    internal bool? CheckAutoRotationControlled()
    {
        var lease = Registrations.Values
            .Where(l => l.AutoRotationControlled.Count != 0)
            .OrderByDescending(l => l.LastUpdated)
            .FirstOrDefault();

        return lease?.AutoRotationControlled[0];
    }

    /// <summary>
    ///     Adds a registration for Auto-Rotation control to a lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease" />
    /// </param>
    /// <param name="newState">Whether to enabled Auto-Rotation.</param>
    /// <seealso cref="Provider.SetAutoRotationState" />
    internal void AddRegistrationForAutoRotation(Guid lease, bool newState)
    {
        var registration = Registrations[lease];

        // Always [0], not an actual add
        registration.AutoRotationControlled[0] = newState;

        registration.LastUpdated = DateTime.Now;
        AutoRotationStateUpdated = DateTime.Now;

        Logging.Log($"{registration.PluginName}: Auto-Rotation state updated");
    }

    /// <summary>
    ///     Adds a registration for the current Job to a lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease" />
    /// </param>
    /// <seealso cref="Provider.SetCurrentJobAutoRotationReady" />
    internal void AddRegistrationForCurrentJob(Guid lease)
    {
        var registration = Registrations[lease];

        var currentJob = (Job)CustomComboFunctions.LocalPlayer!.ClassJob.RowId;
        var job = currentJob.ToString();
        registration.JobsControlled[currentJob] = true;

        registration.LastUpdated = DateTime.Now;
        JobsUpdated = DateTime.Now;

        Logging.Log($"{registration.PluginName}: Registered Current Job ({job})");
    }

    /// <summary>
    ///     Removes a registration from the IPC service, cancelling the lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease" />
    /// </param>
    /// <param name="cancellationReason">
    ///     The <see cref="CancellationReason" /> for cancelling the lease.
    /// </param>
    /// <param name="additionalInfo">
    ///     Any additional information to log and provide with the cancellation.
    /// </param>
    /// <remarks>
    ///     Will call the <see cref="Lease.Callback" /> method if one was
    ///     provided.
    /// </remarks>
    internal void RemoveRegistration
    (Guid lease, CancellationReason cancellationReason,
        string additionalInfo = "")
    {
        Registrations[lease].Cancel(cancellationReason, additionalInfo);
        Registrations.Remove(lease);

        // Bust the UI cache
        AutoRotationStateUpdated = DateTime.Now;
        AutoRotationConfigsUpdated = DateTime.Now;
        JobsUpdated = DateTime.Now;
        CombosUpdated = DateTime.Now;
        OptionsUpdated = DateTime.Now;
    }

    #endregion

    #region Fine-Grained Combo Methods

    internal void AddRegistrationForCombo
        (Guid lease, string combo, bool newState, bool newAutoState)
    {
        throw new NotImplementedException();
    }

    internal void AddRegistrationForOption
        (Guid lease, string combo, bool newState)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    ///     Checks if Auto-Rotation's state is controlled by a lease.
    /// </summary>
    /// <param name="option">
    ///     The Auto-Rotation configuration option to check.
    /// </param>
    /// <returns>
    ///     The state the Auto-Rotation configuration is controlled to, or
    ///     <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.GetAutoRotationConfigControlled" />
    internal int? CheckAutoRotationConfigControlled
        (AutoRotationConfigOption option)
    {
        var lease = Registrations.Values
            .Where(l => l.AutoRotationConfigsControlled.ContainsKey(option))
            .OrderByDescending(l => l.LastUpdated)
            .FirstOrDefault();

        return lease?.AutoRotationConfigsControlled[option];
    }

    /// <summary>
    ///     Checks if a lease controls the current job.
    /// </summary>
    /// <returns>
    ///     The state the current job is controlled to, or <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.IsCurrentJobAutoRotationReady" />
    /// <seealso cref="Provider.IsCurrentJobConfiguredOn" />
    /// <seealso cref="Provider.IsCurrentJobAutoModeOn" />
    internal bool? CheckCurrentJobControlled()
    {
        var currentJob = (Job)CustomComboFunctions.LocalPlayer!.ClassJob.RowId;

        var lease = Registrations.Values
            .Where(l => l.JobsControlled.ContainsKey(currentJob))
            .OrderByDescending(l => l.LastUpdated)
            .FirstOrDefault();

        return lease?.JobsControlled[currentJob];
    }

    /// <summary>
    ///     Checks if a combo is controlled by a lease.
    /// </summary>
    /// <param name="combo">The combo internal name to check.</param>
    /// <returns>
    ///     The <see cref="ComboStateKeys">states</see> the combo is controlled to,
    ///     or <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.GetComboState" />
    internal (bool enabled, bool autoMode)? CheckComboControlled(string combo)
    {
        var customComboPreset = (CustomComboPreset)
            Enum.Parse(typeof(CustomComboPreset), combo, true);

        var lease = Registrations.Values
            .Where(l => l.CombosControlled.ContainsKey(customComboPreset))
            .OrderByDescending(l => l.LastUpdated)
            .FirstOrDefault();

        return lease?.CombosControlled[customComboPreset];
    }

    /// <summary>
    ///     Checks if a combo option is controlled by a lease.
    /// </summary>
    /// <param name="option">The combo option internal name to check.</param>
    /// <returns>
    ///     The state the combo option is controlled to, or <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.GetComboOptionState" />
    internal bool? CheckComboOptionControlled(string option)
    {
        var customComboPreset = (CustomComboPreset)
            Enum.Parse(typeof(CustomComboPreset), option, true);

        var lease = Registrations.Values
            .Where(l => l.OptionsControlled.ContainsKey(customComboPreset))
            .OrderByDescending(l => l.LastUpdated)
            .FirstOrDefault();

        return lease?.OptionsControlled[customComboPreset];
    }

    #endregion

    #region Helper Methods

    /// <summary>
    ///     Checks if a lease exists.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease" />
    /// </param>
    /// <returns>Whether the lease exists.</returns>
    internal bool CheckLeaseExists(Guid lease) =>
        Registrations.ContainsKey(lease);

    /// <summary>
    ///     Checks how many sets are still available for a lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease" />
    /// </param>
    /// <returns>
    ///     The number of sets available for the lease, or <c>null</c> if the lease
    ///     does not exist.
    /// </returns>
    /// <seealso cref="MaxLeaseConfigurations" />
    internal int? CheckLeaseConfigurationsAvailable(Guid lease) =>
        Registrations.TryGetValue(lease, out var value)
            ? MaxLeaseConfigurations - value.SetsLeased
            : null;

    /// <summary>
    ///     Suspend all leases. Called when IPC is disabled remotely.
    /// </summary>
    /// <seealso cref="Helper.IPCEnabled" />
    /// <seealso cref="RemoveRegistration" />
    internal void SuspendLeases()
    {
        Logging.Warn(
            "IPC has been disabled remotely.\n" +
            "Suspending all leases."
        );

        // dispose every lease in _registrations
        foreach (var registration in Registrations.Values)
            RemoveRegistration(
                registration.ID, CancellationReason.AllServicesSuspended
            );
    }

    #region Checking for plugin being unloaded

    private int _framesSinceLastCheck;

    private bool _checkingLeaseePluginsUnloaded;

    /// <summary>
    ///     Initializes the Leasing service, and registers leasee unloading checks.
    /// </summary>
    public Leasing()
    {
        Svc.Framework.Update += CheckIfLeaseePluginsUnloaded;
    }

    /// <summary>
    ///     Checks currently loaded plugins against leases.<br />
    ///     Will run when
    ///     <see cref="DalamudReflector.RegisterOnInstalledPluginsChangedEvents">
    ///         OnInstalledPluginsChanged
    ///     </see>
    ///     is triggered.<br />
    ///     This method is registered to trigger off those events in the
    ///     <see cref="Leasing()">ctor</see>.
    /// </summary>
    private void CheckIfLeaseePluginsUnloaded(IFramework _)
    {
        if (_framesSinceLastCheck < 500 || _checkingLeaseePluginsUnloaded)
        {
            _framesSinceLastCheck++;
            return;
        }

        _checkingLeaseePluginsUnloaded = true;

        var plugins = Svc.PluginInterface
            .InstalledPlugins
            .Where(p => p.IsLoaded)
            .Select(p => p.InternalName).ToList();
        var leasesCopy = new Dictionary<Guid, Lease>(Registrations);

        foreach (var (lease, registration) in leasesCopy)
            if (!plugins.Contains(registration.InternalPluginName))
                RemoveRegistration(
                    lease, CancellationReason.LeaseePluginDisabled
                );

        _checkingLeaseePluginsUnloaded = false;
        _framesSinceLastCheck = 0;
    }

    #endregion

    #endregion

    #region Blacklist functionality

    /// <summary>
    ///     List of plugin names that have been revoked by the user.<br />
    ///     Trys to prevent a plugin from immediately re-registering after being
    ///     revoked by the user.
    /// </summary>
    /// <value>
    ///     <b>Key:</b> The former lease ID of the plugin.<br />
    ///     <b>Values:</b><br />
    ///     <b>Item1:</b> The internal plugin name.<br />
    ///     <b>Item2:</b> The <see cref="Lease.ConfigurationsHash" /> of the
    ///     previous lease.<br />
    ///     <b>Item3:</b> The time the lease was revoked.
    /// </value>
    /// <remarks>
    ///     The blacklisting is cleared after 5 minutes.
    /// </remarks>
    private Dictionary<Guid, (string, byte[], DateTime)>
        _userRevokedTemporaryBlacklist = new();

    /// <summary>
    ///     Removes entries from the blacklist that are older than 5 minutes.
    /// </summary>
    private void CleanOutdatedBlacklistEntries()
    {
        var now = DateTime.Now;
        Dictionary<Guid, (string, byte[], DateTime)> blacklistCopy =
            new(_userRevokedTemporaryBlacklist);
        foreach (var (lease, (_, _, time)) in blacklistCopy)
            if (now - time > TimeSpan.FromMinutes(5))
                _userRevokedTemporaryBlacklist.Remove(lease);
    }

    /// <summary>
    ///     Checks if a lease was revoked by the user and is still blacklisted.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease" />.
    /// </param>
    /// <returns>If the lease is blacklisted.</returns>
    internal bool CheckBlacklist(Guid lease)
    {
        CleanOutdatedBlacklistEntries();

        return _userRevokedTemporaryBlacklist.ContainsKey(lease);
    }

    /// <summary>
    ///     Checks if a plugin name was revoked by the user and is still blacklisted.
    /// </summary>
    /// <param name="internalPluginName">
    ///     The internal name of the plugin that was revoked.
    /// </param>
    /// <returns>If the plugin's name is blacklisted.</returns>
    internal bool CheckBlacklist(string internalPluginName)
    {
        CleanOutdatedBlacklistEntries();

        return _userRevokedTemporaryBlacklist.Values
            .Any(entry => entry.Item1 == internalPluginName);
    }

    /// <summary>
    ///     Checks if a configuration hash revoked by the user and is still
    ///     blacklisted.<br />
    ///     The only blacklist check that can trigger after establishing a new lease.
    /// </summary>
    /// <param name="hash">
    ///     The configuration hash of the plugin that was revoked.
    /// </param>
    /// <returns>If the hash is blacklisted.</returns>
    internal bool CheckBlacklist(byte[] hash)
    {
        CleanOutdatedBlacklistEntries();

        return _userRevokedTemporaryBlacklist.Values
            .Any(entry => entry.Item2.SequenceEqual(hash));
    }

    #endregion
}
