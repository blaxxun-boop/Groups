﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace Groups;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
public class Groups : BaseUnityPlugin
{
	private const string ModName = "Groups";
	private const string ModVersion = "1.1.15";
	private const string ModGUID = "org.bepinex.plugins.groups";

	public static Group? ownGroup;

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	public static ConfigEntry<int> maximumGroupSize = null!;
	public static ConfigEntry<Toggle> friendlyFire = null!;
	public static ConfigEntry<Color> friendlyNameColor = null!;
	public static ConfigEntry<string> ignoreList = null!;
	public static ConfigEntry<Color> groupChatColor = null!;
	public static ConfigEntry<Vector2> groupInterfaceAnchor = null!;
	public static ConfigEntry<KeyboardShortcut> groupPingHotkey = null!;
	public static ConfigEntry<Toggle> horizontalGroupInterface = null!;
	public static ConfigEntry<GroupLeaderDisplayOption> groupLeaderDisplay = null!;
	public static ConfigEntry<Color> groupLeaderColor = null!;
	public static ConfigEntry<float> spaceBetweenGroupMembers = null!;
	public static ConfigEntry<BlockInvitation> blockInvitations = null!;

	private static int configOrder = 0;

	private static readonly ConfigSync configSync = new(ModName) { CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, new ConfigDescription(description.Description, description.AcceptableValues, description.Tags.Length == 0 ? new object[] { new ConfigurationManagerAttributes { Order = configOrder-- } } : description.Tags));

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	public enum Toggle
	{
		On = 1,
		Off = 0
	}

	public enum GroupLeaderDisplayOption
	{
		Disabled,
		Icon,
		Color
	}

	public enum BlockInvitation
	{
		Never,
		Always,
		[Description("While PvP enabled")]
		PvP,
		[Description("While Enemy Player Nearby")]
		Enemy
	}

	private class ConfigurationManagerAttributes
	{
		[UsedImplicitly] public int? Order;
		[UsedImplicitly] public bool? Browsable;
	}

	public void Awake()
	{
		Assembly? bepinexConfigManager = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ConfigurationManager");

		Type? configManagerType = bepinexConfigManager?.GetType("ConfigurationManager.ConfigurationManager");
		object? configManager = configManagerType == null ? null : BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(configManagerType);

		void reloadConfigDisplay() => configManagerType?.GetMethod("BuildSettingList")!.Invoke(configManager, Array.Empty<object>());

		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, new ConfigDescription("If on, only server admins can change the configuration."));
		configSync.AddLockingConfigEntry(serverConfigLocked);
		maximumGroupSize = config("1 - General", "Maximum size for groups", 5, new ConfigDescription("Maximum size for groups.", new AcceptableValueRange<int>(2, 10)));
		friendlyFire = config("1 - General", "Friendly fire in groups", Toggle.Off, new ConfigDescription("If members of the same group can damage each other in PvP."));
		groupChatColor = config("2 - Display", "Color of the group chat", new Color(0f, 1f, 0f), new ConfigDescription("The color for messages in your group."), false);
		friendlyNameColor = config("2 - Display", "Name color for group members", new Color(0f, 1f, 0f), new ConfigDescription("The color for names of members of the own group, if you see them in the world."), false);
		friendlyNameColor.SettingChanged += (_, _) => Map.UpdateMapPinColor();
		groupInterfaceAnchor = config("2 - Display", "Position of the group interface", new Vector2(-875, 310), new ConfigDescription("Sets the anchor position of the group interface."), false);
		groupInterfaceAnchor.SettingChanged += Interface.AnchorGroupInterface;
		horizontalGroupInterface = config("2 - Display", "Horizontal group interface", Toggle.Off, new ConfigDescription("Aligns the group interface horizontally, instead of vertically."), false);
		horizontalGroupInterface.SettingChanged += Interface.UpdateGroupInterfaceSpacing;
		groupLeaderDisplay = config("2 - Display", "Group leader display", GroupLeaderDisplayOption.Icon, new ConfigDescription("How the leader of the group is displayed."), false);
		ConfigurationManagerAttributes colorDisplay = new() { Order = --configOrder, Browsable = groupLeaderDisplay.Value == GroupLeaderDisplayOption.Color };
		groupLeaderDisplay.SettingChanged += (_, _) =>
		{
			colorDisplay.Browsable = groupLeaderDisplay.Value == GroupLeaderDisplayOption.Color;
			reloadConfigDisplay();
		};
		groupLeaderColor = config("2 - Display", "Group leader color", new Color(0.6f, 0.6f, 0.2f), new ConfigDescription("Color of the group leader, if using the group leader color display option.", null, colorDisplay), false);
		spaceBetweenGroupMembers = config("2 - Display", "Space between group members", 75f, new ConfigDescription("The space between group members in the group display."), false);
		spaceBetweenGroupMembers.SettingChanged += Interface.UpdateGroupInterfaceSpacing;
		groupPingHotkey = config("3 - Other", "Group ping modifier key", new KeyboardShortcut(KeyCode.LeftAlt), new ConfigDescription("Modifier key that has to be pressed while pinging the map, to make the map ping visible to group members only."), false);
		ignoreList = config("3 - Other", "Names of people who cannot invite you", "", new ConfigDescription("Ignore group invitations from people on this list. Comma separated."), false);
		blockInvitations = config("3 - Other", "Block all invitations", BlockInvitation.Never, new ConfigDescription("Can be used to block all invitations. Optionally, only block invitations while PvP is enabled."), false);

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);

		Interface.Init();
		Map.Init();

		InvokeRepeating(nameof(updatePositon), 0, 2);
	}

	private void updatePositon()
	{
		if (Player.m_localPlayer is { } player && ownGroup is not null && !ZNet.instance.m_publicReferencePosition)
		{
			foreach (PlayerReference reference in ownGroup.playerStates.Keys.Where(r => r.peerId != ZDOMan.instance.GetMyID()))
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(reference.peerId, "Groups UpdatePosition", player.transform.position);
			}
		}
	}

	[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
	public class FriendlyFirePatch
	{
		private static bool Prefix(Character __instance, HitData hit)
		{
			if (__instance == Player.m_localPlayer && hit.GetAttacker() is Player attacker && hit.m_statusEffect != "Staff_shield")
			{
				if (friendlyFire.Value == Toggle.Off && ownGroup is not null && ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayer(attacker)))
				{
					return false;
				}
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(Game), nameof(Game.Shutdown))]
	public class LeaveGroupOnLogout
	{
		private static void Postfix()
		{
			ownGroup = null;
		}
	}
}
