﻿using System.Collections.Generic;
using System.Linq;

namespace Groups;

public class Group
{
	private PlayerReference _leader;

	public PlayerReference leader
	{
		get => _leader;
		set
		{
			if (value != _leader)
			{
				_leader = value;
				API.InvokeLeaderChanged(value);
			}
		}
	}

	public readonly Dictionary<PlayerReference, PlayerState> playerStates;

	public Group(PlayerReference leader, PlayerState playerState)
	{
		this.leader = leader;
		playerStates = new Dictionary<PlayerReference, PlayerState>
		{
			{ leader, playerState },
		};
	}

	public class PlayerState
	{
		public float health;
		public float maxHealth;
		public long playerId;

		public static PlayerState read(ZPackage group) => new()
		{
			health = group.ReadSingle(),
			maxHealth = group.ReadSingle(),
			playerId = group.ReadLong(),
		};

		public void write(ZPackage group)
		{
			group.Write(health);
			group.Write(maxHealth);
			group.Write(playerId);
		}

		public static PlayerState fromLocal() => new() { health = Player.m_localPlayer.GetHealth(), maxHealth = Player.m_localPlayer.GetMaxHealth(), playerId = Game.instance.m_playerProfile.GetPlayerID() };
	}

	private bool CheckGroupFull() => playerStates.Count >= Groups.maximumGroupSize.Value;

	public bool AddMember(PlayerReference player, PlayerState playerState)
	{
		if (playerStates.ContainsKey(player))
		{
			return false;
		}

		if (CheckGroupFull())
		{
			return false;
		}

		foreach (PlayerReference p in playerStates.Keys.ToArray())
		{
			if (p != player)
			{
				ZPackage state = new();
				playerState.write(state);
				ZRoutedRpc.instance.InvokeRoutedRPC(p.peerId, "Groups AddMember", player.ToString(), state);
			}
			ZRoutedRpc.instance.InvokeRoutedRPC(p.peerId, "Groups AddMessage", Localization.instance.Localize("$groups_player_joined", player.name));
		}
		return true;
	}

	public bool RemoveMember(PlayerReference player, bool self = false)
	{
		if (playerStates.ContainsKey(player))
		{
			foreach (PlayerReference p in playerStates.Keys.ToArray())
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(p.peerId, "Groups UpdateGroup", player.ToString(), "Member Removed");
				if (p != player)
				{
					string message = Localization.instance.Localize(self ? "$groups_player_left" : "$groups_player_removed", player.name);
					ZRoutedRpc.instance.InvokeRoutedRPC(p.peerId, "Groups AddMessage", message);
				}
			}

			if (!self)
			{
				ZRoutedRpc.instance.InvokeRoutedRPC(player.peerId, "Groups AddMessage", Localization.instance.Localize("$groups_self_removed"));
			}

			return true;
		}

		return false;
	}

	public bool PromoteMember(PlayerReference player, bool sendToLeader = false)
	{
		if (player == leader)
		{
			return false;
		}

		if (playerStates.ContainsKey(player))
		{
			foreach (PlayerReference p in playerStates.Keys)
			{
				if (p != leader || sendToLeader)
				{
					ZRoutedRpc.instance.InvokeRoutedRPC(p.peerId, "Groups UpdateGroup", player.ToString(), "Member Promoted");
				}
				ZRoutedRpc.instance.InvokeRoutedRPC(p.peerId, "Groups AddMessage", Localization.instance.Localize("$groups_player_promoted", player.name));
			}
			leader = player;
			return true;
		}

		return false;
	}

	public void Leave()
	{
		API.InvokeGroupLeft();

		ChatCommands.ToggleGroupsChat(false);
		PlayerReference ownReference = PlayerReference.fromPlayer(Player.m_localPlayer);

		if (leader == ownReference && playerStates.Count > 1)
		{
			PromoteMember(playerStates.Keys.First(p => p != ownReference));
		}
		RemoveMember(PlayerReference.fromPlayer(Player.m_localPlayer), true);
	}
}
