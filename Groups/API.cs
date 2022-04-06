using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace Groups;

[PublicAPI]
public class API
{
	public static event Action? joinedGroup;
	public static event Action? leftGroup;

	public static event Action<PlayerReference>? memberJoined;
	public static event Action<PlayerReference>? memberLeft;

	public static event Action<PlayerReference>? leaderChanged;

	internal static void InvokeGroupJoined() => joinedGroup?.Invoke();
	internal static void InvokeGroupLeft() => leftGroup?.Invoke();
	internal static void InvokeMemberJoined(PlayerReference player) => memberJoined?.Invoke(player);
	internal static void InvokeMemberLeft(PlayerReference player) => memberLeft?.Invoke(player);
	internal static void InvokeLeaderChanged(PlayerReference player) => leaderChanged?.Invoke(player);

	public static bool IsLoaded()
	{
#if API
		return false;
#else
		return true;
#endif
	}

	public static int GetMaxGroupSize()
	{
#if API
		return 0;
#else
		return Groups.maximumGroupSize.Value;
#endif
	}

	public static List<PlayerReference> GroupPlayers()
	{
#if API
		return new List<PlayerReference>();
#else
		return Groups.ownGroup is not null ? Groups.ownGroup.playerStates.Keys.ToList() : new List<PlayerReference>();
#endif
	}

	public static PlayerReference? GetLeader()
	{
#if API
		return null;
#else
		return Groups.ownGroup?.leader ?? null;
#endif
	}

	public static bool CreateNewGroup()
	{
#if API
		return false;
#else
		LeaveGroup();
		Groups.ownGroup = new Group(PlayerReference.fromPlayer(Player.m_localPlayer), Group.PlayerState.fromLocal());
		InvokeGroupJoined();

		return true;
#endif
	}

	public static bool WriteToGroup(string message)
	{
#if API
		return false;
#else
		if (Groups.ownGroup is not null)
		{
			foreach (PlayerReference p in Groups.ownGroup.playerStates.Keys)
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(p.peerId, "Groups AddMessage", message);
			}

			return true;
		}
		return false;
#endif
	}

	public static bool LeaveGroup()
	{
#if API
		return false;
#else
		Groups.ownGroup?.Leave();
		Groups.ownGroup = null;
		return true;
#endif
	}

	public static bool JoinGroup(PlayerReference targetPlayer)
	{
#if API
		return false;
#else
		if (Groups.ownGroup is not null)
		{
			LeaveGroup();
		}

		ZPackage playerState = new();
		Group.PlayerState.fromLocal().write(playerState);
		ZRoutedRpc.instance.InvokeRoutedRPC(targetPlayer.peerId, "Groups AcceptInvitation", playerState);

		return true;
#endif
	}

	public static bool PromoteToLeader(PlayerReference groupMember)
	{
#if API
		return false;
#else
		if (Groups.ownGroup is not null)
		{
			Groups.ownGroup.PromoteMember(groupMember, true);

			return true;
		}

		return false;
#endif
	}

	public static bool ForcePlayerIntoOwnGroup(PlayerReference targetPlayer)
	{
#if API
		return false;
#else
		if (Groups.ownGroup is null)
		{
			CreateNewGroup();
		}

		ZRoutedRpc.instance.InvokeRoutedRPC(targetPlayer.peerId, "Groups ForcedInvitation");

		return true;
#endif
	}
}
