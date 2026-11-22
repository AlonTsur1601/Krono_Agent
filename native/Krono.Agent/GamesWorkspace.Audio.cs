using Windows.Media.Core;
using Windows.Media.Playback;

namespace Krono_Agent;

public sealed partial class GamesWorkspace
{
    enum GameSound { Click, Pickup, Success, Failure }
    MediaPlayer? gamePlayer;

    void PlayGameSound(GameSound sound)
    {
        if (previewOnly || disposed) return;
        var file = sound switch {
            GameSound.Click => "click.wav",
            GameSound.Pickup => "pickup.wav",
            GameSound.Success => "success.wav",
            _ => "failure.mp3",
        };
        try {
            gamePlayer ??= new MediaPlayer();
            gamePlayer.Volume = sound == GameSound.Failure ? .12 : .30;
            gamePlayer.Source = MediaSource.CreateFromUri(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "GameAudio", file)));
            gamePlayer.Play();
        } catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException) {
            System.Diagnostics.Debug.WriteLine(error.Message);
        }
    }
}
