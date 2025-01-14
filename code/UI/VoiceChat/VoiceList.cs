using Sandbox;
using Sandbox.UI;
using System.Linq;

namespace Home;

[StyleSheet]
public class HomeVoiceList : Panel
{
	public static HomeVoiceList Current { get; internal set; }

	public HomeVoiceList()
	{
		Current = this;
	}

	// Show a voice list entry locally
    // public override void Tick()
	// {
    //     if(Voice.IsRecording)
    //     {
    //         var entry = ChildrenOfType<HomeVoiceEntry>().FirstOrDefault( x => x.Friend.Id == Game.LocalClient.SteamId );
    //         if ( entry == null ) entry = new HomeVoiceEntry( this, Game.LocalClient.SteamId );

    //         entry.Update( Voice.Level );
    //     }
	// }

	public void OnVoicePlayed( long steamId, float level )
	{
		var entry = ChildrenOfType<HomeVoiceEntry>().FirstOrDefault( x => x.Friend.Id == steamId );
		if ( entry == null ) entry = new HomeVoiceEntry( this, steamId );

		entry.Update( level );
	}
}
