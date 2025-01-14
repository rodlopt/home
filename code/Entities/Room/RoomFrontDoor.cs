using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Editor;

namespace Home;

/// <summary>
/// This entity defines the spawn point of the player in first person shooter gamemodes.
/// </summary>
[Library( "home_front_door" ), HammerEntity, SupportsSolid]
[Model( Archetypes = ModelArchetype.animated_model )]
[DoorHelper( "movedir", "movedir_islocal", "movedir_type", "distance" )]
[RenderFields, VisGroup( VisGroup.Dynamic )]
[Title( "Room Front Door" ), Category( "Room" ), Icon( "house" )]
public partial class RoomFrontDoor : DoorEntity
{
    /// <summary>
    /// The name of this location
    /// </summary>
    [Property( Title = "Room ID" )]
    [Net] public int RoomId { get; set; } = 1;

    public Transform StartTransform;
    public RoomFrontDoorNumber Number;
    RealTimeSince TimeSinceLastUpdate = 0f;

    public override void Spawn()
    {
        base.Spawn();

        Tags.Add("complex");
        Tags.Add("solid");

        

        StartTransform = Transform;
        SetState(RoomState.Vacant);
    }

    public override void ClientSpawn()
    {
        base.ClientSpawn();

        StartTransform = Transform;

        Number = new RoomFrontDoorNumber();
        Number.NumberLabel.Text = RoomId.ToString();
        Number.Position = Position + Rotation.Backward * 2.125f + Rotation.Right * 24f + Rotation.Up * 4f;
        Number.Rotation = Rotation * Rotation.From( new Angles( 0, 180, 0 ) );
        SetNumberState(RoomState.Vacant);
    }

    [GameEvent.Tick.Server]
    public void OnTick()
    {
        if ( TimeSinceLastUpdate > 8f )
        {
            RoomController room = Entity.All.OfType<RoomController>().FirstOrDefault( x => x.Id == RoomId );
            SetNumberState(room.State);
            TimeSinceLastUpdate = 0f;
        }
    }

    public void SetState(RoomState state)
    {
        switch ( state )
        {
            case RoomState.Vacant:
                Close();
                Lock();
                break;
            case RoomState.Open:
                Unlock();
                break;
            case RoomState.Locked:
                Close();
                Lock();
                break;
            case RoomState.FriendsOnly:
                Unlock();
                break;
        }

        SetNumberState(state);
    }

    [ClientRpc]
    public void SetNumberState(RoomState state)
    {
        Number?.SetState(state);
    }

}