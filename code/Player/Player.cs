
using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.UI;
using Sandbox.Component;
using System.ComponentModel;
using System.Text.Json;
using Home.Util;

namespace Home;

public partial class HomePlayer : AnimatedEntity
{
    /// <summary>
	/// The PlayerController takes player input and moves the player. This needs
	/// to match between client and server. The client moves the local player and
	/// then checks that when the server moves the player, everything is the same.
	/// This is called prediction. If it doesn't match the player resets everything
	/// to what the server did, that's a prediction error.
	/// You should really never manually set this on the client - it's replicated so
	/// that setting the class on the server will automatically network and set it
	/// on the client.
	/// </summary>
	[Net, Predicted]
	public PawnController Controller { get; set; }

	public static HomePlayer Local => Game.LocalPawn as HomePlayer;

    /// <summary>
	/// This is used for noclip mode
	/// </summary>
	[Net, Predicted]
	public PawnController DevController { get; set; }

    [Net, Predicted] public Entity ActiveChild { get; set; }
	[ClientInput] public Vector3 InputDirection { get; protected set; }
	[ClientInput] public Entity ActiveChildInput { get; set; }
	[ClientInput] public Angles ViewAngles { get; set; }
	public Angles OriginalViewAngles { get; private set; }

    /// <summary>
	/// Player's inventory for entities that can be carried. See <see cref="BaseCarriable"/>.
	/// </summary>
	public Inventory Inventory { get; protected set; }

    private DamageInfo lastDamage;

	public float FieldOfView { get; set; } = 90;
	public float ThirdPersonZoom { get; set; } = 0;
	public Angles ThirdPersonRotation { get; set; }
	
	private Particles PukeParticle = null;

	[ClientInput] public string LastRoomName { get; set; }

	Nametag Nametag;


	public RealTimeSince TimeSinceSpawned { get; set; }

    /// <summary>
    /// The clothing container is what dresses the citizen
    /// </summary>
    public ClothingContainer Clothing = new();
	[Net] public string ClothingString { get; set; } = "";

	[Net] public string Location { get; set; } = "N/A";
	[Net, Change] public RoomController Room { get; set; } = null;

	[BindComponent] public AnimatorComponent Animator { get; }
	[BindComponent] public FlashlightComponent Flashlight { get; }

    TimeSince timeSinceDied;

    public HomePlayer()
    {
        Inventory = new Inventory( this );
    }

    public HomePlayer(IClient client) : this()
    {
		// Load Administrative Stuffs
		InitRole(client);
		
		LoadPlayerDataClientRpc(To.Single(client));
    }

	public override void Spawn()
	{
		EnableLagCompensation = true;

		Tags.Add( "player" );

		base.Spawn();

		Components.GetOrCreate<AnimatorComponent>();
		Components.GetOrCreate<FlashlightComponent>();

		Room = null;
	}

	public override void ClientSpawn()
	{
		base.ClientSpawn();

		Nametag = new Nametag(this);

		LastRoomName = Cookie.GetString("home.last_room_name");
	}

	public virtual void Respawn()
	{
        SetModel("models/citizen/citizen.vmdl");

		TimeSinceSpawned = 0;

		ResetController();

        if(DevController is NoclipController)
        {
            DevController = null;
        }

        this.ClearWaterLevel();
        EnableAllCollisions = true;
        EnableDrawing = true;
        EnableHideInFirstPerson = true;
        EnableShadowInFirstPerson = true;

        Dress();

        Game.AssertServer();

		LifeState = LifeState.Alive;
		Health = 100;
		Velocity = Vector3.Zero;

		CreateHull();

		CreateHands();

		if(Input.VR.IsActive)
			SetBodyGroup("Hands", 1); // Hide hands

		GameManager.Current?.MoveToSpawnpoint( this );
		ResetInterpolation();
	}

	[ConVar.Server]
	public static async void LoadDefaultOutfit(long steamId)
	{
		var client = Game.Clients.FirstOrDefault(x => x.SteamId == steamId);
		if(client == null) return;
		if(client.Pawn is not HomePlayer player) return;

		await player.LoadDefaultOutfit();
	}

	public async Task LoadDefaultOutfit()
	{
		Game.AssertServer();
		Clothing.LoadFromClient(Client);
		ClothingString = Clothing.Serialize();

		await Dress();
	}


    /// <summary>
	/// Return the controller to use. Remember any logic you use here needs to match
	/// on both client and server. This is called as an accessor every tick.. so maybe
	/// avoid creating new classes here or you're gonna be making a ton of garbage!
	/// </summary>
	public virtual PawnController GetActiveController()
	{
		if ( DevController != null ) return DevController;

		return Controller;
	}

	/// <summary>
	/// Called every tick to simulate the player. This is called on the
	/// client as well as the server (for prediction). So be careful!
	/// </summary>
	public override void Simulate( IClient cl )
	{
		if ( ActiveChildInput.IsValid() && ActiveChildInput.Owner == this )
		{
			ActiveChild = ActiveChildInput;
		}

		if ( LifeState == LifeState.Dead )
		{
			// Respawn
			if ( (timeSinceDied > 3 || Input.Pressed("click")) && Game.IsServer )
			{
				Respawn();
			}

			return;
		}
		
		var controller = GetActiveController();
		controller?.Simulate( cl, this );

		if(LifeState != LifeState.Alive) return;

		if(controller != null)
		{
			EnableSolidCollisions = !controller.HasTag( "noclip" );
			Animator?.Simulate();
		}

		TickPlayerUse();
		SimulateActiveChild(cl, ActiveChild);

		CheckRotate();
		SetVrAnimProperties();

		// Flashlight
		Flashlight?.Simulate();
		
		// Third Person Toggle
		if(Input.Pressed("view"))
		{
			if(ThirdPersonZoom > 0f) ThirdPersonZoom = 0f;
			else ThirdPersonZoom = 130f;
		}

		if(Input.Down("puke") && PukeParticle == null)
		{
			PukeParticle = Particles.Create("particles/puke.vpcf", this, "hat");
		}
		else if(!Input.Down("puke") && PukeParticle != null)
		{
			PukeParticle.Destroy();
			PukeParticle = null;
		}

		LeftHand?.Simulate(cl);
		RightHand?.Simulate(cl);
	}

	[ConCmd.Server("noclip")]
	static void DoPlayerNoclip()
	{
		if(ConsoleSystem.Caller.Pawn is HomePlayer player)
		{
			if(player.HasModeratorPermissions() || ConsoleSystem.GetValue("sv_cheats") == "1")
			{
				if(player.DevController is NoclipController)
				{
					player.DevController = null;
					player.Components.RemoveAny<NoclipController>();
				}
				else
				{
					var controller = new NoclipController();
					player.Components.Add(controller);
					player.DevController = controller;
				}
			}
		}
	}

	// "kill" command that kills the player who called it
	[ConCmd.Server("kill")]
	static void DoPlayerSuicide()
	{
		if(ConsoleSystem.Caller.Pawn is HomePlayer player && player.TimeSinceSpawned > 2f)
		{
			player.TakeDamage(new DamageInfo {Damage = player.Health * 99 });
		}
	}

    public override void FrameSimulate( IClient cl )
	{
		Flashlight?.FrameSimulate();

		if(ThirdPersonZoom > 0f) // THIRD PERSON CAM
		{
			Camera.Rotation = ViewAngles.ToRotation() * ThirdPersonRotation.ToRotation();
		}
		else // FIRST PERSON CAM
		{
			Camera.Rotation = ViewAngles.ToRotation();
			ThirdPersonRotation = default;
		}
		var targetFOV = Game.Preferences.FieldOfView - (Input.Down("Zoom") ? 40 : 0);
		FieldOfView = MathX.LerpTo( FieldOfView, targetFOV, Time.Delta * 10f );
		Camera.FieldOfView = Screen.CreateVerticalFieldOfView( FieldOfView );

		if(Input.MouseWheel != 0)
		{

			// ZOOM CAMERA IN/OUT
			float previousZoom = ThirdPersonZoom;
			ThirdPersonZoom = MathX.Clamp(ThirdPersonZoom - Input.MouseWheel * 10, 10, 400);
			if(Input.MouseWheel > 0f && ThirdPersonZoom <= 10)
			{
				ThirdPersonZoom = 0f;
			}
			else if(Input.MouseWheel < 0f && ThirdPersonZoom < 10)
			{
				ThirdPersonZoom = 10;
			}
		}

		if ( LifeState != LifeState.Alive && Corpse.IsValid() ) // RAGDOLL CAM
		{
			Corpse.EnableDrawing = true;

			var pos = Corpse.GetBoneTransform( 0 ).Position + Vector3.Up * 10;
			var targetPos = pos + Camera.Rotation.Backward * 100;

			var tr = Trace.Ray( pos, targetPos )
				.WithAnyTags( "solid" )
				.Ignore( this )
				.Radius( 8 )
				.Run();

			Camera.Position = tr.EndPosition;
			Camera.FirstPersonViewer = null;
		}
		else if ( ThirdPersonZoom > 0f ) // THIRD PERSON CAM
		{
			Camera.FirstPersonViewer = null;

			// Vector3 targetPos;
			// var center = Position + Vector3.Up * 64;

			// var pos = center;
			// var rot = Camera.Rotation * Rotation.FromAxis( Vector3.Up, -16 );

			// float distance = ThirdPersonZoom * Scale;
			// targetPos = pos + rot.Right * ((CollisionBounds.Mins.x + 16) * Scale);
			// targetPos += rot.Forward * -distance;

			var eyePos = Vector3.Up * (64f * Scale) * (Data?.Height ?? 1f);
			var pos = Position + eyePos;
			var targetPos = pos + (Camera.Rotation).Backward * ThirdPersonZoom;

			var tr = Trace.Ray( pos, targetPos )
				.WithAnyTags( "solid" )
				.Ignore( this )
				.Radius( 8 )
				.Run();

			Camera.Position = tr.EndPosition;
		}
		else // FIRST PERSON CAM
		{
			Camera.Position = EyePosition;
			Camera.FirstPersonViewer = this;
			Camera.Main.SetViewModelCamera( 90f );
		}

		LeftHand?.FrameSimulate(cl);
		RightHand?.FrameSimulate(cl);
	}

    /// <summary>
	/// Applies flashbang-like ear ringing effect to the player.
	/// </summary>
	/// <param name="strength">Can be approximately treated as duration in seconds.</param>
	[ClientRpc]
	public void Deafen( float strength )
	{
		Audio.SetEffect( "flashbang", strength, velocity: 20.0f, fadeOut: 4.0f * strength );
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();

		Nametag?.Delete();
	}

	public void SetController(PawnController controller)
	{
		Game.AssertServer();
		Components.RemoveAny<PawnController>();
		Components.Add(controller);
		Controller = controller;
	}

	public void ResetController()
	{
		// TODO: VR Controller
		// if(Input.VR.IsActive)
		// {
		// 	Controller = new WalkControllerVR();
		// }
		// else
		{
        	SetController(new WalkController());
		}
	}

	public async Task Dress()
	{
		Clothing.Deserialize(ClothingString);
		Log.Info(">>>>> DRESSING THE PLAYER <<<<<");
		foreach(var item in Clothing.Clothing)
		{
			if(item is HomeClothing hcloth && !string.IsNullOrEmpty(hcloth.CloudModel))
			{
				await hcloth.MountPackage();
			}
		}
		ClothingHelper.DressEntity(this, Clothing);
	}

	public override void OnKilled()
	{
		HomeGame.Current?.OnKilled(this);

		timeSinceDied = 0;
		LifeState = LifeState.Dead;
		StopUsing();

		Client?.AddInt("deaths", 1);

        BecomeRagdollOnClient( Velocity, lastDamage.Position, lastDamage.Force, lastDamage.BoneIndex, lastDamage.HasTag( "bullet" ), lastDamage.HasTag( "blast" ) );

		Components.RemoveAny<PawnController>();

		EnableAllCollisions = false;
		EnableDrawing = false;

		foreach ( var child in Children )
		{
			child.EnableDrawing = false;
		}

		DeleteHands();
	}

    /// <summary>
	/// Create a physics hull for this player. The hull stops physics objects and players passing through
	/// the player. It's basically a big solid box. It also what hits triggers and stuff.
	/// The player doesn't use this hull for its movement size.
	/// </summary>
	public virtual void CreateHull()
	{
		var height = (Data?.Height ?? 1f);
		Log.Info("HULL HEIGHT:");
		Log.Info(height);
		SetupPhysicsFromAABB( PhysicsMotionType.Keyframed, new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 72 * height ) );

		//var capsule = new Capsule( new Vector3( 0, 0, 16 ), new Vector3( 0, 0, 72 - 16 ), 32 );
		//var phys = SetupPhysicsFromCapsule( PhysicsMotionType.Keyframed, capsule );


		//	phys.GetBody(0).RemoveShadowController();

		// TODO - investigate this? if we don't set movetype then the lerp is too much. Can we control lerp amount?
		// if so we should expose that instead, that would be awesome.
		EnableHitboxes = true;
	}

	public WorldInput WorldInput = new();

    /// <summary>
	/// Called from the gamemode, clientside only.
	/// </summary>
	public override void BuildInput()
	{
		OriginalViewAngles = ViewAngles;
		InputDirection = Input.AnalogMove;

		if ( Input.StopProcessing )
			return;

		WorldInput.Ray = new Ray(Camera.Position, Camera.Rotation.Forward);
		WorldInput.MouseLeftPressed = Input.Down( "click" );
		WorldInput.MouseRightPressed = Input.Down( "rightclick" );

		Angles look = Input.AnalogLook;

		// Room interactions
		if(Room != null && Input.Down("menu"))
		{
			if(PlacingGuide.IsPlacing)
			{
				if(Input.Released("click"))
				{
					if(CanPlace) TryPlace();
					else TryPickup();
				}

				if(Input.MouseWheel != 0)
				{
					PlacingAngle += Input.MouseWheel * 15f;
					PlacingAngle = MathF.Round(PlacingAngle / 15f) * 15f;
				}
			}
			else
			{
				if(Input.Pressed("click"))
				{
					// Cast a ray to see if we clicked on a HomePlaceable
					var tr = Trace.Ray(new Ray(Camera.Position, Screen.GetDirection(Mouse.Position)), 1000f)
						.Ignore(this)
						.Run();

					if(tr.Entity.Components.Get<PlaceableComponent>() is PlaceableComponent component && component.OwnerId == Game.LocalClient.SteamId)
					{
						SetPlacing(tr.Entity.Root);
						var dragging = Game.RootPanel.AddChild<HomeInventoryDragging>();
			            dragging.Placeable = HomePlaceable.Find(component.PlaceableId);
						dragging.SetBackgroundImage(dragging.Placeable.GetThumbnail().Result);

						PlacingAngle = component.LocalAngle;
					}
				}
				else if(Input.Pressed("rightclick"))
				{
					// Cast a ray to see if we clicked on a HomePlaceable
					var tr = Trace.Ray(new Ray(Camera.Position, Screen.GetDirection(Mouse.Position)), 1000f)
						.Ignore(this)
						.Run();
					
					if(tr.Entity.Root.Components.Get<PlaceableComponent>() is PlaceableComponent component && component.OwnerId == Game.LocalClient.SteamId)
					{
						// Destroy all previous context menus
						foreach(var menu in Game.RootPanel.ChildrenOfType<PlaceableContextMenu>())
						{
							menu.Delete();
						}

						// Create new context menu
						var contextMenu = Game.RootPanel.AddChild<PlaceableContextMenu>();				
						contextMenu.SetEntity(tr.Entity.Root);
						contextMenu.Style.Left = Mouse.Position.x * contextMenu.ScaleFromScreen;
						contextMenu.Style.Top = Mouse.Position.y * contextMenu.ScaleFromScreen;
					}
				}
			}
		}
		
		if(Input.Down("rightclick") && ThirdPersonZoom > 0f)
		{
			ThirdPersonRotation += look;
			look = default;
		}
		else
		{
			ThirdPersonRotation = ThirdPersonRotation.LerpTo(default, Time.Delta * 10f);
		}

		if ( ViewAngles.pitch > 90f || ViewAngles.pitch < -90f )
		{
			look = look.WithYaw( look.yaw * -1f );
		}

		var viewAngles = ViewAngles;
		viewAngles += look;
		viewAngles.pitch = viewAngles.pitch.Clamp( -89f, 89f );
		viewAngles.roll = 0f;
		ViewAngles = viewAngles.Normal;

		ActiveChild?.BuildInput();

		GetActiveController()?.BuildInput();
	}

    /// <summary>
	/// A generic corpse entity
	/// </summary>
	public ModelEntity Corpse { get; set; }


	TimeSince timeSinceLastFootstep = 0;

    /// <summary>
	/// A footstep has arrived!
	/// </summary>
	public override void OnAnimEventFootstep( Vector3 pos, int foot, float volume )
	{
		if ( LifeState != LifeState.Alive )
			return;

		if ( !Game.IsClient )
			return;

		if ( timeSinceLastFootstep < 0.2f )
			return;

		volume *= FootstepVolume();

		timeSinceLastFootstep = 0;

		//DebugOverlay.Box( 1, pos, -1, 1, Color.Red );
		//DebugOverlay.Text( pos, $"{volume}", Color.White, 5 );

		var tr = Trace.Ray( pos, pos + Vector3.Down * 20 )
			.Radius( 1 )
			.Ignore( this )
			.Run();

		if ( !tr.Hit ) return;

		tr.Surface.DoFootstep( this, tr, foot, volume );
	}

	/// <summary>
	/// Allows override of footstep sound volume.
	/// </summary>
	/// <returns>The new footstep volume, where 1 is full volume.</returns>
	public virtual float FootstepVolume()
	{
		return Velocity.WithZ( 0 ).Length.LerpInverse( 0.0f, 200.0f ) * 5.0f;
	}

    public override void StartTouch( Entity other )
	{
		if ( Game.IsClient ) return;

		if ( other is PickupTrigger )
		{
			StartTouch( other.Parent );
			return;
		}

        // TODO: Other pickups on touch?
		// Inventory?.Add( other, Inventory.Active == null );
	}

    /// <summary>
	/// This isn't networked, but it's predicted. If it wasn't then when the prediction system
	/// re-ran the commands LastActiveChild would be the value set in a future tick, so ActiveEnd
	/// and ActiveStart would get called multiple times and out of order, causing all kinds of pain.
	/// </summary>
	[Predicted]
	Entity LastActiveChild { get; set; }

	/// <summary>
	/// Simulated the active child. This is important because it calls ActiveEnd and ActiveStart.
	/// If you don't call these things, viewmodels and stuff won't work, because the entity won't
	/// know it's become the active entity.
	/// </summary>
	public virtual void SimulateActiveChild( IClient cl, Entity child )
	{
		if ( LastActiveChild != child )
		{
			OnActiveChildChanged( LastActiveChild, child );
			LastActiveChild = child;
		}

		if ( !LastActiveChild.IsValid() )
			return;

		if ( LastActiveChild.IsAuthority )
		{
			LastActiveChild.Simulate( cl );
		}
	}

	/// <summary>
	/// Called when the Active child is detected to have changed
	/// </summary>
	public virtual void OnActiveChildChanged( Entity previous, Entity next )
	{
		if ( previous is HomeBaseCarriable previousBc )
		{
			previousBc?.ActiveEnd( this, previousBc.Owner != this );
		}

		if ( next is HomeBaseCarriable nextBc )
		{
			nextBc?.ActiveStart( this );
		}
	}

	public override void TakeDamage( DamageInfo info )
	{
		if ( LifeState == LifeState.Dead )
			return;

		base.TakeDamage( info );

		this.ProceduralHitReaction( info );

		//
		// Add a score to the killer
		//
		if ( LifeState == LifeState.Dead && info.Attacker != null )
		{
			if ( info.Attacker.Client != null && info.Attacker != this )
			{
				info.Attacker.Client.AddInt( "kills" );
			}
		}

		if ( info.HasTag( "blast" ) )
		{
			Deafen( To.Single( Client ), info.Damage.LerpInverse( 0, 60 ) );
		}
	}

	// public override void OnChildAdded( Entity child )
	// {
	// 	Inventory?.OnChildAdded( child );
	// }

	// public override void OnChildRemoved( Entity child )
	// {
	// 	Inventory?.OnChildRemoved( child );
	// }

	/// <summary>
	/// Position a player should be looking from in world space.
	/// </summary>
	[Browsable( false )]
	public Vector3 EyePosition
	{
		get => Transform.PointToWorld( EyeLocalPosition );
		set => EyeLocalPosition = Transform.PointToLocal( value );
	}

	/// <summary>
	/// Position a player should be looking from in local to the entity coordinates.
	/// </summary>
	[Net, Predicted, Browsable( false )]
	public Vector3 EyeLocalPosition { get; set; }

	/// <summary>
	/// Rotation of the entity's "eyes", i.e. rotation for the camera when this entity is used as the view entity.
	/// </summary>
	[Browsable( false )]
	public Rotation EyeRotation
	{
		get => Transform.RotationToWorld( EyeLocalRotation );
		set => EyeLocalRotation = Transform.RotationToLocal( value );
	}

	/// <summary>
	/// Rotation of the entity's "eyes", i.e. rotation for the camera when this entity is used as the view entity. In local to the entity coordinates.
	/// </summary>
	[Net, Predicted, Browsable( false )]
	public Rotation EyeLocalRotation { get; set; }

	/// <summary>
	/// Override the aim ray to use the player's eye position and rotation.
	/// </summary>
	public override Ray AimRay => new Ray( EyePosition, EyeRotation.Forward );

	[ClientRpc]
	public void SaveLayout(string name, bool removeOwner = false)
	{
		// Check the player and their variables
		if(Game.LocalPawn is not HomePlayer player) return;
		if(player.Room == null) return;

		// TODO: Ask if player wants to overwrite layout if exists?

		// Save the layout
		RoomLayout layout = player.Room.SaveLayout(name);

		// Add the layout to the local layouts
		if(player.RoomLayouts.Find(l => l.Name == name) == null)
		{
			player.RoomLayouts.Add(layout);
		}
		else
		{
			var index = player.RoomLayouts.FindIndex(l => l.Name == layout.Name);
			Log.Info(index);
			player.RoomLayouts[index] = layout;
		}

		if(removeOwner)
		{
			RoomController.RemoveOwnerServer();
		}

		// Save the layout to a local file
		FileSystem.Data.WriteJson(player.Client.SteamId + "/layouts/" + layout.Name + ".json", layout);

		NotificationPanel.AddEntry(To.Single(player), "💾 Saved layout \"" + name + "\"", "", 5);
	}

	public void LoadLayout(string name)
	{
		// Check the player and their variables
		if(Game.LocalPawn is not HomePlayer player) return;
		if(player.Room == null) return;

		// Check the layout
		RoomLayout layout = player.RoomLayouts.FirstOrDefault(l => l.Name == name, null);
		if(layout == null)
		{
			NotificationPanel.AddEntry(To.Single(player), "📁 COULD NOT LOAD LAYOUT \"" + name + "\"", "", 5);
			return;
		}

		// Load the layout
		player.HomeUploadData = Json.Serialize(layout);
		RoomController.LoadLayout();
		NotificationPanel.AddEntry(To.Single(player), "📁 Loaded layout \"" + name + "\"", "", 5);
	}

	[ClientRpc]
	public void LoadLayoutClientRpc(string name)
	{
		LoadLayout(name);
	}

	public void OnRoomChanged(RoomController oldRoom, RoomController newRoom)
	{
		Log.Info("Room changed from " + (oldRoom == null ? "null" : oldRoom.Name) + " to " + (newRoom == null ? "null" : newRoom.Name));
		if(Game.IsClient && Game.LocalPawn == this && oldRoom == null && newRoom != null)
		{
			LoadLayout(newRoom.Name);
		}
	}

	[ConCmd.Server]
	public static async void ChangeOutfit(int networkIdent, string outfit, float height = -1.0f)
	{
		if(Entity.FindByIndex<HomePlayer>(networkIdent) is not HomePlayer player) return;
		await player.ChangeOutfit(outfit, height);
		if(height != -1.0f) player.SavePlayerDataClientRpc(To.Single(player.Client));
	}

	public async Task ChangeOutfit(string outfit, float height = -1.0f)
	{
		Game.AssertServer();

		ClothingString = outfit;
		await Dress();
		
		if(height >= 0)
			SetHeight(height);
	}

	[ConCmd.Server]
	public static void ChangePlayerModel(string name)
	{
		if(ConsoleSystem.Caller.Pawn is not HomePlayer player) return;
		HomePlayermodel plymodel = HomePlayermodel.Find(name);
		if(plymodel == null) return;
		player.SetModel(plymodel.Model);
		player.Dress();
	}

}