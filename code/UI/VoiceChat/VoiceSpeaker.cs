﻿namespace Sandbox.UI;

[StyleSheet]
public class HomeVoiceSpeaker : Label
{
	private float VoiceLevel = 0.0f;

	public HomeVoiceSpeaker()
	{
		StyleSheet.Load( "/UI/VoiceChat/VoiceSpeaker.scss" );
		Text = "mic";
	}

	public override void Tick()
	{
		VoiceLevel = VoiceLevel.LerpTo( Voice.Level, Time.Delta * 40.0f );
		SetClass( "active", Voice.IsRecording );

		var tr = new PanelTransform();
		tr.AddScale( 1.0f.LerpTo( 1.5f, VoiceLevel ) );
		Style.Transform = tr;
	}
}
