using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using UnityEngine.UI;

namespace Groups;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class Groups : BaseUnityPlugin
{
	private const string ModName = "Groups";
	private const string ModVersion = "1.0.0";
	private const string ModGUID = "org.bepinex.plugins.groups";

	public static Group? ownGroup;
	private static GameObject? groupDialog;
	private static GameObject? groupMemberFirst;
	private static long pendingInvitationSenderId;
	private static readonly Color defaultColor = new(1f, 0.7176471f, 0.3602941f);
	public static bool groupChatActive = false;
	private static Sprite groupLeaderIcon = null!;
	private static Sprite groupMapPinIcon = null!;
	private static readonly List<Terminal.ConsoleCommand> terminalCommands = new();

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	public static ConfigEntry<int> maximumGroupSize = null!;
	private static ConfigEntry<Toggle> friendlyFire = null!;
	private static ConfigEntry<Color> friendlyNameColor = null!;
	private static ConfigEntry<string> ignoreList = null!;
	private static ConfigEntry<Color> groupChatColor = null!;
	public static ConfigEntry<Vector2> groupInterfaceAnchor = null!;

	private static readonly ConfigSync configSync = new(ModName) { CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private enum Toggle
	{
		On = 1,
		Off = 0
	}

	public void Awake()
	{
		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, new ConfigDescription("If on, only server admins can change the configuration."));
		configSync.AddLockingConfigEntry(serverConfigLocked);
		maximumGroupSize = config("1 - General", "Maximum size for groups", 5, new ConfigDescription("Maximum size for groups.", new AcceptableValueRange<int>(2, 10)));
		friendlyFire = config("1 - General", "Friendly fire in groups", Toggle.Off, new ConfigDescription("If members of the same group can damage each other in PvP."));
		friendlyNameColor = config("1 - General", "Name color for group members", new Color(0f, 1f, 0f), new ConfigDescription("The color for names of members of the own group, if you see them in the world."), false);
		friendlyNameColor.SettingChanged += (_, _) => UpdateMapPinColor();
		ignoreList = config("1 - General", "Names of people who cannot invite you", "", new ConfigDescription("Ignore group invitations from people on this list. Comma separated."), false);
		groupChatColor = config("1 - General", "Color of the group chat", new Color(0f, 1f, 0f), new ConfigDescription("The color for messages in your group."), false);
		groupInterfaceAnchor = config("1 - General", "Position of the group interface", new Vector2(-875, 310), new ConfigDescription("Sets the anchor position of the group interface."), false);
		groupInterfaceAnchor.SettingChanged += AnchorGroupInterface;

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);

		groupLeaderIcon = Helper.loadSprite("leader.png", 32, 32);
		groupMapPinIcon = Helper.loadSprite("groupPlayerIcon.png", 64, 64);

		UpdateMapPinColor();

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

	private static void onUpdatePosition(long senderId, Vector3 position)
	{
		ZNet.PlayerInfo player = ZNet.instance.m_players.FirstOrDefault(id => id.m_characterID.m_userID == senderId);
		ZNet.instance.m_players.Remove(player);
		player.m_position = position;
		ZNet.instance.m_players.Add(player);
	}

	private static void AnchorGroupInterface(object sender, EventArgs e)
	{
		if (groupMemberFirst is not null)
		{
			groupMemberFirst.transform.parent.localPosition = groupInterfaceAnchor.Value;
		}
	}

	private static void UpdateMapPinColor()
	{
		Color[]? pixels = Helper.loadTexture("groupPlayerIcon.png").GetPixels();
		for (int i = 0; i < pixels.Length; ++i)
		{
			if (pixels[i].r > 0.5 && pixels[i].b < 0.5 && pixels[i].g < 0.5)
			{
				pixels[i] = friendlyNameColor.Value;
			}
		}
		groupMapPinIcon.texture.SetPixels(pixels);
		groupMapPinIcon.texture.Apply();
	}

	[HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePlayerPins))]
	private class ChangeGroupMemberPin
	{
		private static void Postfix(Minimap __instance)
		{
			if (ownGroup is null)
			{
				return;
			}

			for (int index = 0; index < __instance.m_tempPlayerInfo.Count; ++index)
			{
				Minimap.PinData playerPin = __instance.m_playerPins[index];
				ZNet.PlayerInfo playerInfo = __instance.m_tempPlayerInfo[index];
				if (playerPin.m_name == playerInfo.m_name)
				{
					playerPin.m_icon = ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayerInfo(playerInfo)) ? groupMapPinIcon : __instance.GetSprite(Minimap.PinType.Player);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
	private class AddGroupDialog
	{
		private static void Postfix()
		{
			groupDialog = Instantiate(Menu.instance.m_quitDialog.gameObject, Hud.instance.m_rootObject.transform, true);
			Button.ButtonClickedEvent noClicked = new();
			noClicked.AddListener(onDeclineInvitation);
			groupDialog.transform.Find("dialog/Button_no").GetComponent<Button>().onClick = noClicked;
			Button.ButtonClickedEvent yesClicked = new();
			yesClicked.AddListener(onAcceptInvitation);
			groupDialog.transform.Find("dialog/Button_yes").GetComponent<Button>().onClick = yesClicked;
		}
	}

	[HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
	public class AddChatCommands
	{
		private static void Postfix()
		{
			terminalCommands.Clear();

			terminalCommands.Add(new Terminal.ConsoleCommand("invite", "invite someone to your group", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length < "invite".Length || Chat.instance == null)
				{
					return;
				}

				string playerName = args.FullLine.Substring(7);

				if (string.Compare(playerName, Player.m_localPlayer.GetHoverName(), StringComparison.OrdinalIgnoreCase) == 0)
				{
					Chat.instance.AddString("You cannot invite yourself.");
					return;
				}

				long targetId = ZNet.instance.m_players.FirstOrDefault(p => string.Compare(playerName, p.m_name, StringComparison.OrdinalIgnoreCase) == 0).m_characterID.m_userID;
				if (targetId == 0)
				{
					Chat.instance.AddString($"{playerName} is not online.");
					return;
				}

				if (ownGroup is null)
				{
					ownGroup = new Group(PlayerReference.fromPlayer(Player.m_localPlayer), Group.PlayerState.fromLocal());
					API.InvokeGroupJoined();
				}

				if (ownGroup.leader == PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					ZRoutedRpc.instance.InvokeRoutedRPC(targetId, "Groups InvitePlayer", Player.m_localPlayer.GetHoverName());
					Chat.instance.AddString($"Sent an invitation to {playerName}.");
				}
				else
				{
					Chat.instance.AddString("Only the leader of a group can send out invitations.");
				}
			}), optionsFetcher: () => ZNet.instance.m_players.Select(p => p.m_name).ToList()));

			terminalCommands.Add(new Terminal.ConsoleCommand("kick", "removes someone from your group", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length < "kick".Length || Chat.instance == null)
				{
					return;
				}

				if (ownGroup is null)
				{
					Chat.instance.AddString("You are not in a group.");

					return;
				}

				if (ownGroup.leader != PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					Chat.instance.AddString("Only the leader of a group can kick members.");

					return;
				}

				string playerName = args.FullLine.Substring(5);

				if (string.Compare(playerName, Player.m_localPlayer.GetHoverName(), StringComparison.OrdinalIgnoreCase) == 0)
				{
					Chat.instance.AddString("You cannot kick yourself. Please use /leave instead.");

					return;
				}

				if (!ownGroup.RemoveMember(ownGroup.playerStates.Keys.FirstOrDefault(p => string.Compare(p.name, playerName, StringComparison.OrdinalIgnoreCase) == 0)))
				{
					Chat.instance.AddString($"{playerName} is not in this group.");
				}

			}), optionsFetcher: () => ownGroup?.playerStates.Keys.Select(p => p.name).Where(n => n != Player.m_localPlayer.GetHoverName()).ToList() ?? new List<string>()));

			terminalCommands.Add(new Terminal.ConsoleCommand("promote", "promotes someone to group leader", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length < "promote".Length || Chat.instance == null)
				{
					return;
				}

				if (ownGroup is null)
				{
					Chat.instance.AddString("You are not in a group.");

					return;
				}

				if (ownGroup.leader != PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					Chat.instance.AddString("Only the leader of a group can promote someone.");

					return;
				}

				string playerName = args.FullLine.Substring(8);

				if (!ownGroup.PromoteMember(ownGroup.playerStates.Keys.FirstOrDefault(p => string.Compare(p.name, playerName, StringComparison.OrdinalIgnoreCase) == 0)))
				{
					Chat.instance.AddString($"{playerName} is not in this group.");
				}

			}), optionsFetcher: () => ownGroup?.playerStates.Keys.Select(p => p.name).Where(n => n != Player.m_localPlayer.GetHoverName()).ToList() ?? new List<string>()));

			_ = new Terminal.ConsoleCommand("leave", "leaves your current group", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length < "leave".Length || Chat.instance == null)
				{
					return;
				}

				if (ownGroup is null)
				{
					Chat.instance.AddString("You are not in a group.");

					return;
				}

				ownGroup.Leave();
			}));

			_ = new Terminal.ConsoleCommand("p", "toggles the group chat on", (Terminal.ConsoleEvent)(args =>
			{
				if (args.FullLine.Length < "p".Length || Chat.instance == null)
				{
					return;
				}

				if (ownGroup is null)
				{
					Chat.instance.AddString("You are not in a group.");

					return;
				}

				if (args.FullLine.Length > 2)
				{
					string message = args.FullLine.Substring(2);

					foreach (PlayerReference player in ownGroup.playerStates.Keys)
					{
						ZRoutedRpc.instance.InvokeRoutedRPC(player.peerId, "Groups ChatMessage", Player.m_localPlayer.GetHoverName(), message);
					}
				}
				else
				{
					groupChatActive = !groupChatActive;
				}
			}));
		}
	}

	private static void UpdateAutoCompletion()
	{
		foreach (Terminal.ConsoleCommand command in terminalCommands)
		{
			command.m_tabOptions = null;
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.Update))]
	private class PlaceHolder
	{
		private static void Postfix(Chat __instance)
		{
			__instance.m_input.transform.Find("Placeholder").GetComponent<Text>().text = groupChatActive ? "Write to group ..." : "Write something ...";
		}
	}

	[HarmonyPatch(typeof(Game), nameof(Game.Start))]
	private class AddRPCs
	{
		private static void Postfix()
		{
			ZRoutedRpc.instance.Register<string>("Groups AddMessage", (_, message) =>
			{
				Chat.instance.AddString(message);
				Chat.instance.m_hideTimer = 0f;
			});
			ZRoutedRpc.instance.Register<string, string>("Groups ChatMessage", onChatMessageReceived);
			ZRoutedRpc.instance.Register<string>("Groups InvitePlayer", onInvitationReceived);
			ZRoutedRpc.instance.Register("Groups ForcedInvitation", onForcedInvitationReceived);
			ZRoutedRpc.instance.Register<ZPackage>("Groups AcceptInvitation", onInvitationAccepted);
			ZRoutedRpc.instance.Register<ZPackage>("Groups AcceptInvitationResponse", onInvitationAcceptedResponse);
			ZRoutedRpc.instance.Register<string, string>("Groups UpdateGroup", onUpdateGroup);
			ZRoutedRpc.instance.Register<string, ZPackage>("Groups AddMember", onNewGroupMember);
			ZRoutedRpc.instance.Register<float, float>("Groups UpdateHealth", onUpdateHealth);
			ZRoutedRpc.instance.Register<Vector3>("Groups UpdatePosition", onUpdatePosition);
		}
	}

	[HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PlayerList))]
	private class RemoveFromGroupOnLogoutAndPreservePosition
	{
		private static void Prefix(ZNet __instance, out Dictionary<long, Vector3> __state)
		{
			__state = new Dictionary<long, Vector3>();

			if (ownGroup is null)
			{
				return;
			}

			foreach (ZNet.PlayerInfo playerInfo in __instance.m_players.Where(p => ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayerId(p.m_characterID.m_userID))))
			{
				__state[playerInfo.m_characterID.m_userID] = playerInfo.m_position;
			}
		}

		private static void Postfix(ZNet __instance, Dictionary<long, Vector3> __state)
		{
			UpdateAutoCompletion();

			if (ownGroup is null)
			{
				return;
			}

			List<ZNet.PlayerInfo> playerInfos = new();
			foreach (ZNet.PlayerInfo playerInfo in __instance.m_players)
			{
				ZNet.PlayerInfo info = playerInfo;
				if (ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayerId(playerInfo.m_characterID.m_userID)) && playerInfo.m_characterID != Player.m_localPlayer?.GetZDOID())
				{
					if (__state.TryGetValue(playerInfo.m_characterID.m_userID, out Vector3 position))
					{
						if (!playerInfo.m_publicPosition)
						{
							info.m_position = position;
						}
					}
					info.m_publicPosition = true;
				}
				playerInfos.Add(info);
			}
			__instance.m_players = playerInfos;

			List<PlayerReference> online = __instance.m_players.Select(PlayerReference.fromPlayerInfo).ToList();

			if (ownGroup.leader.peerId != ZDOMan.instance.GetMyID())
			{
				bool leaderIsOnline = online.Contains(ownGroup.leader);
				if (leaderIsOnline || ownGroup.playerStates.Keys.OrderBy(p => p.peerId).First(online.Contains).peerId != ZDOMan.instance.GetMyID())
				{
					return;
				}

				ownGroup.PromoteMember(PlayerReference.fromPlayerId(ZDOMan.instance.GetMyID()));
			}

			foreach (PlayerReference player in ownGroup.playerStates.Keys.Except(online).ToArray())
			{
				ownGroup.RemoveMember(player, true);
			}
		}
	}

	private static void onUpdateGroup(long senderId, string playerReference, string action)
	{
		if (ownGroup is null)
		{
			return;
		}

		switch (action)
		{
			case "Member Removed":
			{
				PlayerReference reference = PlayerReference.fromString(playerReference);
				if (reference == PlayerReference.fromPlayer(Player.m_localPlayer))
				{
					API.InvokeGroupLeft();

					groupChatActive = false;
					ownGroup = null;
				}
				else
				{
					ownGroup.playerStates.Remove(reference);
					API.InvokeMemberLeft(reference);
				}

				UpdateAutoCompletion();

				break;
			}
			case "Member Promoted":
			{
				ownGroup.leader = PlayerReference.fromString(playerReference);

				UpdateAutoCompletion();

				break;
			}
		}
	}

	private static void onNewGroupMember(long senderId, string playerReference, ZPackage playerState)
	{
		if (ownGroup is null)
		{
			return;
		}

		PlayerReference reference = PlayerReference.fromString(playerReference);

		ownGroup.playerStates.Add(reference, Group.PlayerState.read(playerState));
		API.InvokeMemberJoined(reference);

		UpdateAutoCompletion();
	}

	private static void onChatMessageReceived(long senderId, string name, string message)
	{
		Chat.instance.AddString("<color=orange>" + name + "</color>: <color=#" + ColorUtility.ToHtmlStringRGBA(groupChatColor.Value) + ">" + message + "</color>");
		ZDOID playerZDO = ZNet.instance.m_players.FirstOrDefault(p => p.m_characterID.m_userID == senderId).m_characterID;
		if (playerZDO != ZDOID.None && ZNetScene.instance.FindInstance(playerZDO) is { } playerObject && playerObject.GetComponent<Player>() is { } player)
		{
			if (Minimap.instance && Player.m_localPlayer && Minimap.instance.m_mode == Minimap.MapMode.None && Vector3.Distance(Player.m_localPlayer.transform.position, player.GetHeadPoint()) > Minimap.instance.m_nomapPingDistance)
			{
				return;
			}
			Chat.instance.AddInworldText(playerObject, senderId, player.GetHeadPoint(), Talker.Type.Normal, name, "<color=#" + ColorUtility.ToHtmlStringRGBA(groupChatColor.Value) + ">" + message + "</color>");
		}
	}

	private static void onInvitationReceived(long senderId, string name)
	{
		string[] ignored = ignoreList.Value.Split(',');
		if (ignored.Any(s => string.Compare(s.Trim(), name, StringComparison.OrdinalIgnoreCase) == 0))
		{
			return;
		}

		pendingInvitationSenderId = senderId;

		groupDialog!.SetActive(true);
		groupDialog.transform.Find("dialog/Exit").GetComponent<Text>().text = $"{name} invited you to join their group.";
	}

	private static void onForcedInvitationReceived(long senderId)
	{
		API.JoinGroup(PlayerReference.fromPlayerId(senderId));
	}

	private static void onDeclineInvitation()
	{
		groupDialog!.SetActive(false);
	}

	private static void onAcceptInvitation()
	{
		ownGroup?.Leave();

		groupDialog!.SetActive(false);

		ZPackage playerState = new();
		Group.PlayerState.fromLocal().write(playerState);
		ZRoutedRpc.instance.InvokeRoutedRPC(pendingInvitationSenderId, "Groups AcceptInvitation", playerState);
	}

	private static void onInvitationAccepted(long senderId, ZPackage playerState)
	{
		ZPackage group = new();

		if (ownGroup is not null && ownGroup.AddMember(PlayerReference.fromPlayerId(senderId), Group.PlayerState.read(playerState)))
		{
			void AddPlayer(PlayerReference player)
			{
				group.Write(player.ToString());
				ownGroup!.playerStates[player].write(group);
			}

			AddPlayer(ownGroup.leader);
			group.Write(ownGroup.playerStates.Count - 1);
			foreach (PlayerReference player in ownGroup.playerStates.Keys.Where(p => p != ownGroup.leader))
			{
				AddPlayer(player);
			}
			ZRoutedRpc.instance.InvokeRoutedRPC(senderId, "Groups AcceptInvitationResponse", group);

			return;
		}

		ZRoutedRpc.instance.InvokeRoutedRPC(senderId, "Groups AcceptInvitationResponse", group);
	}

	private static void onInvitationAcceptedResponse(long senderId, ZPackage group)
	{
		if (group.Size() == 0)
		{
			Chat.instance.AddString("Joining the group failed. Maybe it's full or doesn't exist anymore.");

			return;
		}

		Group tmp = new(PlayerReference.fromString(group.ReadString()), Group.PlayerState.read(group));
		int memberCount = group.ReadInt();
		for (int i = 0; i < memberCount; ++i)
		{
			tmp.playerStates.Add(PlayerReference.fromString(group.ReadString()), Group.PlayerState.read(group));
		}

		ownGroup = tmp;

		API.InvokeGroupJoined();

		UpdateAutoCompletion();
	}

	[HarmonyPatch(typeof(Menu), nameof(Menu.IsVisible))]
	private class DisablePlayerInputInGroupDialog
	{
		private static void Postfix(ref bool __result)
		{
			if (groupDialog?.activeSelf == true)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))]
	public class ColorNames
	{
		private static void Postfix(Character c, Dictionary<Character, EnemyHud.HudData> ___m_huds)
		{
			if (c is not Player player)
			{
				return;
			}

			GameObject hudBase = ___m_huds[c].m_gui;

			Text playerName = hudBase.transform.Find("Name").GetComponent<Text>();

			playerName.color = ownGroup != null && ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayer(player)) ? friendlyNameColor.Value : defaultColor;
		}
	}

	[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
	public class FriendlyFirePatch
	{
		private static bool Prefix(Character __instance, HitData hit)
		{
			if (__instance == Player.m_localPlayer && hit.GetAttacker() is Player attacker)
			{
				if (friendlyFire.Value == Toggle.Off && ownGroup is not null && ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayer(attacker)))
				{
					return false;
				}
			}

			return true;
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.Awake))]
	public class AddGroupChat
	{
		private static void Postfix(Chat __instance)
		{
			__instance.m_chatBuffer.Insert(__instance.m_chatBuffer.Count - 5, "/p [text] Group chat");
			__instance.m_chatBuffer.Insert(__instance.m_chatBuffer.Count - 5, "/p Toggle group chat");
			__instance.UpdateChat();
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.InputText))]
	public class SendMessageToGroup
	{
		private static void Prefix(Chat __instance)
		{
			if (__instance.m_input.text.Length != 0 && groupChatActive && __instance.m_input.text[0] != '/')
			{
				__instance.m_input.text = "/p " + __instance.m_input.text;
			}
		}
	}

	[HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
	public class AddGroupDisplay
	{
		private static void Postfix(Hud __instance)
		{
			GameObject groups = new("Groups");
			groups.AddComponent<DragNDrop>();
			groups.transform.SetParent(__instance.m_rootObject.transform);
			groups.transform.localPosition = groupInterfaceAnchor.Value;
			groupMemberFirst = Instantiate(EnemyHud.instance.m_baseHudPlayer, groups.transform);
			groupMemberFirst.transform.localPosition = Vector3.zero;
			Transform healthBar = groupMemberFirst.transform.Find("Health");
			healthBar.localScale = new Vector3(1, 3.5f, 1);
			Transform darken = healthBar.transform.Find("darken");
			darken.GetComponent<RectTransform>().sizeDelta = new Vector2(8, 3);

			GameObject leaderImage = new("Leader Icon");
			leaderImage.transform.SetParent(groupMemberFirst.transform);
			Image leaderIcon = leaderImage.AddComponent<Image>();
			leaderIcon.sprite = groupLeaderIcon;
			RectTransform leaderRect = leaderImage.GetComponent<RectTransform>();
			leaderRect.sizeDelta = new Vector2(32, 32);
			leaderRect.localPosition = new Vector2(0, 46);
			leaderImage.SetActive(false);

			GameObject lifeDisplay = new("Life Display Text", typeof(RectTransform));
			lifeDisplay.transform.SetParent(groupMemberFirst.transform, false);
			((RectTransform)lifeDisplay.transform).sizeDelta = new Vector2(300, 50);
			lifeDisplay.transform.localPosition = new Vector3(0, 5);
			Text lifeDisplayText = lifeDisplay.AddComponent<Text>();
			lifeDisplayText.font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(x => x.name == "AveriaSerifLibre-Bold");
			lifeDisplayText.fontSize = 14;
			lifeDisplayText.alignment = TextAnchor.MiddleCenter;
			Outline outline = lifeDisplay.AddComponent<Outline>();
			outline.effectColor = Color.black;
			outline.effectDistance = new Vector2(1, -1);
		}
	}

	[HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
	public class UpdateGroupDisplay
	{
		private static void Postfix()
		{
			Transform groupRoot = groupMemberFirst!.transform.parent;
			groupRoot.gameObject.SetActive(ownGroup is not null);

			if (ownGroup is null)
			{
				return;
			}

			while (groupRoot.childCount < ownGroup.playerStates.Count)
			{
				GameObject newMember = Instantiate(groupMemberFirst, groupRoot, false);
				newMember.transform.localPosition = new Vector3(0, -(groupRoot.childCount - 1) * 75);
			}

			for (int i = 0; i < groupRoot.childCount; ++i)
			{
				Transform member = groupRoot.GetChild(i);
				List<KeyValuePair<PlayerReference, Group.PlayerState>> members = ownGroup.playerStates.ToList();
				bool active = i < ownGroup.playerStates.Count;
				member.gameObject.SetActive(active);
				if (active)
				{
					PlayerReference player = members[i].Key;
					Group.PlayerState playerState = members[i].Value;

					member.Find("Health/health_slow").GetComponent<GuiBar>().SetValue(playerState.health / playerState.maxHealth);
					member.Find("Health/health_fast").GetComponent<GuiBar>().SetValue(playerState.health / playerState.maxHealth);
					member.Find("Life Display Text").GetComponent<Text>().text = Mathf.Ceil(playerState.health) + " / " + Mathf.Ceil(playerState.maxHealth);

					Text memberText = member.Find("Name").GetComponent<Text>();
					memberText.text = player.name;
					memberText.color = friendlyNameColor.Value;
					member.Find("Leader Icon").gameObject.SetActive(player == ownGroup.leader);
				}
			}
		}
	}

	[HarmonyPatch]
	public class BroadcastHealth
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(Character), nameof(Character.SetHealth));
			yield return AccessTools.Method(typeof(Character), nameof(Character.SetMaxHealth));
		}

		private static void Postfix(Character __instance)
		{
			if (ownGroup is not null && __instance == Player.m_localPlayer)
			{
				foreach (PlayerReference player in ownGroup.playerStates.Keys)
				{
					ZRoutedRpc.instance.InvokeRoutedRPC(player.peerId, "Groups UpdateHealth", __instance.GetHealth(), __instance.GetMaxHealth());
				}
			}
		}
	}

	private static void onUpdateHealth(long senderId, float health, float maxHealth)
	{
		if (ownGroup is null || !ownGroup.playerStates.TryGetValue(PlayerReference.fromPlayerId(senderId), out Group.PlayerState playerState))
		{
			return;
		}

		playerState.health = health;
		playerState.maxHealth = maxHealth;
	}
}
