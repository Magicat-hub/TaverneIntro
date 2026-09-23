using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Réglages de la barre LED de la taverne, pilotée par QLC+ en OSC (UDP).
/// Même configuration que l'outil console OscLed : à adapter dans l'Inspector.
/// </summary>
[Serializable]
public class TavernLightsSettings
{
    [Tooltip("Envoyer les commandes à QLC+. Décocher si QLC+ n'est pas lancé (ça ne plante pas, mais autant ne rien envoyer).")]
    public bool enabled = true;

    [Tooltip("IP de la machine où tourne QLC+ (Input/Output Manager > votre univers > plugin OSC).")]
    public string ip = "127.0.0.1";

    [Tooltip("Port UDP d'entrée OSC de QLC+.")]
    public int port = 7700;

    [Tooltip("Univers QLC+ : les channels sont adressés en /<univers>/<channel>.")]
    public int universe = 1;

    [Tooltip("Nombre de LEDs RGB de la barre (channels 1 à 3 x ce nombre).")]
    public int ledCount = 10;

    [Tooltip("Master dimmer de la fixture : mis à fond pour que les couleurs se voient.")]
    public int masterDimmerChannel = 33;

    [Tooltip("Channel du bouton Virtual Console qui lance l'animation de victoire (Chaser « WinAnim ») de chaque équipe : équipe 1, 2, 3... 0 = pas d'animation, couleur fixe directe.")]
    public int[] winAnimationChannels = { 40, 41, 42 };

    [Tooltip("Durée de l'animation de victoire dans QLC+ (s), avant de passer à la couleur fixe de l'équipe.")]
    public float winAnimationSeconds = 10f;

    [Tooltip("Couleur de chaque équipe (équipe 1, 2, 3...). Sert aussi pour le nom et la victoire de l'équipe à l'écran.")]
    public Color32[] teamColors =
    {
        new Color32(255, 0, 197, 255),   // équipe 1 : rose
        new Color32(252, 198, 0, 255),   // équipe 2 : jaune
        new Color32(0, 156, 252, 255),   // équipe 3 : bleu
    };

    [Tooltip("Éteindre la barre (channels RGB à 0) quand une nouvelle partie commence.")]
    public bool turnOffOnNewMatch = true;
}

/// <summary>
/// Envoie à QLC+ les mêmes commandes OSC que l'outil console OscLed : animation de
/// victoire puis couleur fixe de l'équipe, extinction... Tout part sur un thread à part :
/// le jeu ne se fige jamais, même pendant les 10 s de l'animation.
///
/// Une seule connexion UDP pour tout le jeu (classe statique) ; appeler Configure avec
/// les réglages de la scène avant d'utiliser le reste.
/// </summary>
public static class TavernLights
{
    private static TavernLightsSettings settings = new TavernLightsSettings();
    private static UdpClient udp;
    private static IPEndPoint endpoint;
    private static CancellationTokenSource pending;
    private static bool warned;
    private static bool lit;
    private static readonly object sendLock = new object();

    public static void Configure(TavernLightsSettings newSettings)
    {
        settings = newSettings ?? new TavernLightsSettings();
        endpoint = null;
        if (!settings.enabled) return;

        if (!IPAddress.TryParse(settings.ip, out IPAddress address))
        {
            Debug.LogWarning($"[TavernLights] IP invalide : « {settings.ip} ». Lumières désactivées.");
            return;
        }

        endpoint = new IPEndPoint(address, settings.port);
        if (udp == null)
        {
            udp = new UdpClient();
            Application.quitting += Close;
        }
    }

    /// <summary>Couleur de l'équipe (0 = équipe 1), ou <paramref name="fallback"/> si aucune n'est réglée.</summary>
    public static Color TeamColor(int team, Color fallback)
    {
        var colors = settings.teamColors;
        return colors != null && team >= 0 && team < colors.Length ? (Color)colors[team] : fallback;
    }

    /// <summary>
    /// Victoire de l'équipe (0 = équipe 1) : lance son animation « WinAnim » dans QLC+,
    /// attend sa fin, puis laisse la barre allumée dans la couleur de l'équipe.
    /// </summary>
    public static void CelebrateWin(int team)
    {
        if (endpoint == null) return;

        CancelPending();
        var cancel = pending = new CancellationTokenSource();
        var s = settings;
        lit = true;

        Task.Run(async () =>
        {
            try
            {
                SetAll(Color.black);   // repart du noir avant l'animation
                OpenMasterDimmer();

                int channel = s.winAnimationChannels != null && team < s.winAnimationChannels.Length ? s.winAnimationChannels[team] : 0;
                if (channel > 0)
                {
                    PressButton(channel);
                    await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(0f, s.winAnimationSeconds)), cancel.Token);
                }

                if (cancel.IsCancellationRequested) return;
                ApplyColor(TeamColor(team, Color.white));
            }
            catch (TaskCanceledException) { }
            catch (Exception e) { Warn(e); }
        });
    }

    /// <summary>Éteint la barre (seulement si on l'avait allumée) et annule une animation en cours.</summary>
    public static void Off()
    {
        CancelPending();
        if (endpoint == null || !lit || !settings.turnOffOnNewMatch) return;
        lit = false;
        Task.Run(() => SetAll(Color.black));
    }

    private static void CancelPending()
    {
        pending?.Cancel();
        pending = null;
    }

    private static void Close()
    {
        CancelPending();
        udp?.Close();
        udp = null;
        endpoint = null;
    }

    // ------------------------------------------------------------------ commandes QLC+

    private static void OpenMasterDimmer() => Send(settings.masterDimmerChannel, 1f);

    /// <summary>Couleur fixe, envoyée 2 fois à 50 ms d'écart : un paquet UDP perdu ne laisse pas la barre à moitié allumée.</summary>
    private static void ApplyColor(Color color)
    {
        OpenMasterDimmer();
        for (int pass = 0; pass < 2; pass++)
        {
            SetAll(color);
            Thread.Sleep(50);
        }
    }

    private static void SetAll(Color color)
    {
        for (int led = 0; led < settings.ledCount; led++)
        {
            int baseChannel = led * 3 + 1;
            Send(baseChannel, Mathf.Clamp01(color.r));
            Send(baseChannel + 1, Mathf.Clamp01(color.g));
            Send(baseChannel + 2, Mathf.Clamp01(color.b));
        }
    }

    /// <summary>Appui sur un bouton Virtual Console : 1 puis 0, pour que l'appui suivant soit bien un nouveau front.</summary>
    private static void PressButton(int channel)
    {
        Send(channel, 1f);
        Send(channel, 0f);
    }

    private static void Send(int channel, float value)
    {
        var target = endpoint;
        var client = udp;
        if (target == null || client == null) return;

        try
        {
            byte[] packet = BuildOscMessage($"/{settings.universe}/{channel}", value);
            lock (sendLock) client.Send(packet, packet.Length, target);
        }
        catch (Exception e)
        {
            Warn(e);
        }
    }

    private static void Warn(Exception e)
    {
        if (warned) return;
        warned = true;
        Debug.LogWarning($"[TavernLights] Impossible de joindre QLC+ ({settings.ip}:{settings.port}) : {e.Message}");
    }

    /// <summary>Paquet OSC minimal : adresse, type « ,f », puis le float en big-endian.</summary>
    private static byte[] BuildOscMessage(string address, float value)
    {
        using (var stream = new MemoryStream())
        {
            WriteOscString(stream, address);
            WriteOscString(stream, ",f");

            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            stream.Write(bytes, 0, 4);

            return stream.ToArray();
        }
    }

    private static void WriteOscString(MemoryStream stream, string s)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(s);
        stream.Write(bytes, 0, bytes.Length);
        int padding = 4 - (bytes.Length % 4);   // toujours au moins un octet nul, aligné sur 4
        stream.Write(new byte[padding], 0, padding);
    }
}
