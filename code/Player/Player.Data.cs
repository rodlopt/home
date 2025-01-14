using System.Net.WebSockets;
using Sandbox;
using Sandbox.UI;
using Sandbox.UI.Construct;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Home.Util;

namespace Home;


public partial class HomePlayer
{
    [Net] public PlayerData Data { get; set; }
	public List<RoomLayout> RoomLayouts = new List<RoomLayout>();
	[Net] public Pet PetEntity { get; set; }

	public bool NewVariable { get; set; }

	[ConVar.ClientData] public string HomeUploadData { get; set; } = "";

	[ClientRpc]
	public void SavePlayerDataClientRpc()
	{
		Data?.Save();
	}

	[ClientRpc]
	public void LoadPlayerDataClientRpc()
	{
		if(Game.LocalPawn is not HomePlayer player) return;

		// Load player data from client data
		HomeUploadData = FileSystem.Data.ReadAllText(Client.SteamId.ToString() + "/player.json");
		if(string.IsNullOrEmpty(HomeUploadData))
		{
			HomeUploadData = JsonSerializer.Serialize(new PlayerData(Client.SteamId));
		}

		// Load Clothing
		string clothing = Cookie.GetString("home.outfit", "");
		if(clothing != "")
		{
			ClothingString = clothing;
			HomeGUI.UpdateAvatar(clothing);
		}
		else
		{
			clothing = "default";
		}

		// Load Admin Settings
		player.LoadAdmin();

		// Create layout folder structure
		long steamId = Game.LocalClient.SteamId;
		if(!FileSystem.Data.DirectoryExists(steamId.ToString()))
		{
			FileSystem.Data.CreateDirectory(steamId.ToString());
		}
		if(!FileSystem.Data.DirectoryExists(steamId + "/layouts"))
		{
			FileSystem.Data.CreateDirectory(steamId + "/layouts");
		}
		if(!FileSystem.Data.DirectoryExists(steamId + "/layouts/" + Game.Server.MapIdent))
		{
			FileSystem.Data.CreateDirectory(steamId + "/layouts/" + Game.Server.MapIdent);
		}

		// Load local layouts
		foreach(string file in FileSystem.Data.FindFile(steamId + "/layouts/" + Game.Server.MapIdent, "*.json"))
		{
			RoomLayout layout = FileSystem.Data.ReadJson<RoomLayout>(steamId + "/layouts/" + Game.Server.MapIdent + "/" + file);
			player.RoomLayouts.Add(layout);
		}

		// Tell the server we loaded the player's data
		OnPlayerDataLoaded(Client.SteamId, clothing);
	}

	[ConCmd.Server]
	public static async void OnPlayerDataLoaded(long steamId, string clothing = "")
	{
		IClient client = Game.Clients.FirstOrDefault(c => c.SteamId == steamId);
		if(client.Pawn is not HomePlayer player) return;
		Log.Info("Loading player data for " + client.Name);
		player.Data = new PlayerData(steamId);
		player.Data.LoadFromString(client.GetClientData<string>("HomeUploadData"));
		player.LoadBadges();

		// Set Clothing
		if(clothing == "default")
		{
			await player.LoadDefaultOutfit();
		}
		else if(clothing != "")
		{
			await player.ChangeOutfit(clothing);
		}

		// Set Pet
		HomePlayer.SetPet(player.NetworkIdent, player.Data.CurrentPet);
	}

	public bool HasMoney(long amount)
	{
		return Data.Money >= amount;
	}

	public bool GiveMoney(long amount)
	{
		Data.Money += amount;
		Sandbox.Services.Stats.Increment(this.Client, "money_earned", amount);
		SavePlayerDataClientRpc(To.Single(this.Client));
		return true;
	}

	public bool TakeMoney(long amount)
	{
		if(!HasMoney(amount))
			return false;
		Data.Money -= amount;
		Sandbox.Services.Stats.Increment(this.Client, "money_spent", amount);
		SavePlayerDataClientRpc(To.Single(this.Client));
		return true;
	}

	[ConCmd.Server]
	public static void SetHeight(int networkIdent, float height)
	{
		if(Entity.FindByIndex<HomePlayer>(networkIdent) is not HomePlayer player) return;
		player.SetHeight(height);
		if(player.Data != null) player.SavePlayerDataClientRpc(To.Single(player));
	}

	public void SetHeight(float height)
	{
		SetAnimParameter("scale_height", height);
		if(Data != null)
		{
			Data.Height = Math.Clamp(height, 0.5f, 1.5f);
		}
		//player.CreateHull();
	}

	public bool HasPlaceable(string id, int amount = 1)
	{
		return Data.Stash.Any(s => s.Id == id && s.Amount >= amount);
	}

	public bool CanUsePlaceable(string id, int amount = 1)
	{
		return Data.Stash.Any(s => s.Id == id && s.Amount - s.Used >= amount);
	}

	public void GivePlaceable(string id, long amount = 1)
	{
		if(Data.Stash.Any(s => s.Id == id))
		{
			Data.Stash.First(s => s.Id == id).Amount += (int)amount;
		}
		else
		{
			Data.Stash.Add(new StashEntry(this.Client.SteamId, id, (int)amount));
		}
		SavePlayerDataClientRpc(To.Single(this.Client));
	}

	public void GiveClothing(int id)
	{
		if(Data.Clothing.Contains(id)) return;
		Data.Clothing.Add(id);
		SavePlayerDataClientRpc(To.Single(this.Client));
	}

	public void GivePet(int id)
	{
		if(Data.Pets.Contains(id)) return;
		Data.Pets.Add(id);
		SavePlayerDataClientRpc(To.Single(this.Client));
	}

	public bool TakePlaceable(string id, int amount = 1)
	{
		if(!HasPlaceable(id, amount))
			return false;
		Data.Stash.First(s => s.Id == id).Amount -= (int)amount;
		SavePlayerDataClientRpc(To.Single(this.Client));
		return true;
	}

	public bool UsePlaceable(string id, int amount = 1)
	{
		if(!CanUsePlaceable(id, amount))
			return false;
		//Stash.First(s => s.Id == id).Used += amount;
		return true;
	}

	[ClientRpc]
	public void UnusePlaceable(string id, int amount = 1)
	{
		if(!Data.Stash.Any(s => s.Id == id && s.Used >= amount)) return;
		var thing = Data.Stash.First(s => s.Id == id);
		thing.Used -= amount;
		if(thing.Used < 0) thing.Used = 0;
	}

	public void GiveBadge(string id)
	{
		Game.AssertServer();
		var badge = HomeBadge.FindById(id);
		if(badge == null) return;
		if(Data.BadgeIds.Contains(badge.ResourceId)) return;
		Data.BadgeIds.Add(badge.ResourceId);
		Log.Info("Gave badge " + id + " to " + Client.Name);
	}

	[ConCmd.Server]
	public static void SetPet(int networkIdent, int petId)
	{
		if(Entity.FindByIndex<HomePlayer>(networkIdent) is not HomePlayer player) return;
		player.SetPet(petId);
	}

	public void SetPet(int petId)
	{
		if(petId != 0 && !Data.Pets.Contains(petId)) return;
		if(PetEntity.IsValid()) PetEntity.Delete();
		if(petId != 0)
		{
			var pet = HomePet.Find(petId);

			// Getting a type that matches the name
			var entityType = TypeLibrary.GetType<Pet>( pet.ClassName )?.TargetType;
			if ( entityType == null ) return;

			// Creating an instance of that type
			Pet entity = TypeLibrary.Create<Pet>( entityType );
			if(entity == null) return;

			// Setting the entity's position
			entity.Position = Position + Vector3.Up * 20f;
			entity.Rotation = Rotation;
			entity.Transmit = TransmitType.Always;
			entity.Player = this;

			PetEntity = entity;
		}
		Data.CurrentPet = petId;
		SavePlayerDataClientRpc(To.Single(Client));
	}

}