using Sandbox;

namespace Home;

/// <summary>
/// A placeable TV that you can queue media on
/// </summary>
[EditorModel("models/sbox_props/office_chair/office_chair.vmdl")]
public partial class ChairBase : ModelEntity, IUse
{

    [Net] public HomePlayer CurrentUser { get; set; } = null;
    public virtual Transform SeatOffset => Transform.Zero;
    public virtual Transform ExitOffset => Transform.Zero;
    public virtual bool IsUsable( Entity user ) => CurrentUser == null;

    public override void Spawn()
    {
        base.Spawn();
        SetModel("models/sbox_props/office_chair/office_chair.vmdl");
        SetupPhysicsFromModel(PhysicsMotionType.Keyframed);
    }

    public void SetUser(HomePlayer player)
    {
        if(player.Controller is ChairController controller)
        {
            NotificationPanel.AddEntry(To.Single(player), "🚫 You are already sitting in a chair.", "", 3);
            return;
        }
        ChairController chairController = new ChairController();
        chairController.Chair = this;
        player.SetController(chairController);
        CurrentUser = player;
        
        player.SetParent(this, "seat", SeatOffset);
    }

    public void RemoveUser()
    {
        if(CurrentUser == null) return;
        
        CurrentUser.SetParent(null, null, Transform.Zero);

        if(CurrentUser.Controller is ArcadeControllerBase)
        {
            CurrentUser.ResetController();
        }
        CurrentUser = null;
    }

    public virtual bool OnUse(Entity user)
    {
        Game.AssertServer();
        if(user is not HomePlayer player) return false;
        if(CurrentUser == null)
        {
            SetUser(player);
        }
        return false;
    }
}