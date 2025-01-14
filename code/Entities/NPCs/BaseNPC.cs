using Sandbox;
using Editor;
using System.Collections.Generic;
using System.Linq;
using Home.Util;

namespace Home;

/// <summary>
/// Talking to this NPC will allow players to check-in to an available room.
/// </summary>
public partial class BaseNPC : AnimatedEntity, IUse
{
    public virtual string DisplayName => "Unnamed NPC";
    protected virtual string ClothingString => "";

    protected Transform StartingTransform = new();
    ClothingContainer Clothing = new();

    NPCNametag Nametag;

    public virtual bool IsUsable( Entity user ) => true;

    public override void Spawn()
    {
        base.Spawn();

        StartingTransform = Transform;

        // Setup physics
        SetupPhysicsFromAABB( PhysicsMotionType.Keyframed, new Vector3( -16, -16, 0 ), new Vector3( 16, 16, 72 ) );
        EnableHitboxes = true;

        // Set the model and dress it
        SetModel("models/citizen/citizen.vmdl");
        Clothing.Deserialize(ClothingString);
        ClothingHelper.DressEntity(this, Clothing);
    }

    public override void ClientSpawn()
    {
        base.ClientSpawn();

        Nametag = new(this);
    }

    [GameEvent.Tick.Server]
	void Tick()
	{
        // Initialize some variables
        Vector3 targetPos = Position + Rotation.Forward * 100;
        Rotation targetRot = Rotation;

        // Find the nearest player
        IEnumerable<HomePlayer> nearestPlayers = HomePlayer.FindInSphere(Position, 200f).OfType<HomePlayer>();
        if(nearestPlayers.Count() > 0)
        {
            HomePlayer nearestPlayer = nearestPlayers.FirstOrDefault<HomePlayer>();
            targetPos = nearestPlayer.EyePosition + Vector3.Down * 60f;
            targetRot = Rotation.LookAt( targetPos - GetBoneTransform(GetBoneIndex("head")).Position );
        }

        CitizenAnimationHelper animHelper = new CitizenAnimationHelper(this);

        animHelper.WithWishVelocity( Velocity);
        animHelper.WithVelocity( Velocity );
        animHelper.WithLookAt( targetPos, 0.75f, 0.5f, 0.25f);
        animHelper.AimAngle = targetRot;
	}

    public virtual bool OnUse(Entity user)
    {
        return false;
    }

}