using System.Linq;

namespace Groups;

public struct PlayerReference
{
	public static PlayerReference fromPlayerId(long id) => fromPlayerInfo(ZNet.instance.m_players.First(p => p.m_characterID.m_userID == id));
	public static PlayerReference fromPlayerInfo(ZNet.PlayerInfo playerInfo) => new() { peerId = playerInfo.m_characterID.m_userID, name = playerInfo.m_name ?? "" };
	public static PlayerReference fromPlayer(Player player) => player == Player.m_localPlayer ? new PlayerReference { peerId = ZDOMan.instance.GetMyID(), name = Game.instance.GetPlayerProfile().GetName() } : fromPlayerInfo(ZNet.instance.m_players.FirstOrDefault(info => info.m_characterID == player.GetZDOID()));

	public long peerId;
	public string name;

	public static bool operator !=(PlayerReference a, PlayerReference b) => !(a == b);
	public static bool operator ==(PlayerReference a, PlayerReference b) => a.peerId == b.peerId && a.name == b.name;
	public bool Equals(PlayerReference other) => this == other;
	public override bool Equals(object? obj) => obj is PlayerReference other && Equals(other);

	// ReSharper disable NonReadonlyMemberInGetHashCode
	public override int GetHashCode() => (peerId.GetHashCode() * 397) ^ (name?.GetHashCode() ?? 0);
	// ReSharper restore NonReadonlyMemberInGetHashCode

	public override string ToString() => $"{peerId}:{name}";

	public static PlayerReference fromString(string str)
	{
		string[] parts = str.Split(':');
		return new PlayerReference { peerId = long.Parse(parts[0]), name = parts[1] };
	}
}
