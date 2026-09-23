using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Quand on appuie sur Play dans l'Éditeur, le jeu démarre TOUJOURS sur la scène
/// Menu, quelle que soit la scène ouverte (SampleScene, etc.). En quittant le mode
/// Play, on retrouve la scène qu'on était en train d'éditer, sans rien perdre.
///
/// Se désactive/réactive via le menu Tools > Toujours lancer depuis le Menu
/// (pratique pour tester une scène seule). Le réglage est mémorisé par machine.
///
/// Script d'Éditeur uniquement (dossier Editor) : il n'existe pas dans le jeu
/// exporté, où Menu est de toute façon la première scène des Build Settings.
/// </summary>
[InitializeOnLoad]
public static class PlayFromMenu
{
    private const string MenuScenePath = "Assets/Scenes/Menu.unity";
    private const string PrefKey = "TaverneIntro.PlayFromMenu";
    private const string ToggleMenuPath = "Tools/Toujours lancer depuis le Menu";

    static PlayFromMenu()
    {
        // delayCall : l'AssetDatabase n'est pas toujours prête pendant le chargement du domaine.
        EditorApplication.delayCall += Apply;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    private static void Apply()
    {
        if (!Enabled)
        {
            EditorSceneManager.playModeStartScene = null;
            return;
        }

        var menuScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath);
        if (menuScene == null)
        {
            Debug.LogWarning($"[PlayFromMenu] Scène introuvable : {MenuScenePath}. Play démarre sur la scène ouverte.");
            EditorSceneManager.playModeStartScene = null;
            return;
        }

        EditorSceneManager.playModeStartScene = menuScene;
    }

    [MenuItem(ToggleMenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
        Debug.Log(Enabled
            ? "[PlayFromMenu] Play démarre maintenant toujours sur le Menu."
            : "[PlayFromMenu] Play démarre maintenant sur la scène ouverte.");
    }

    [MenuItem(ToggleMenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(ToggleMenuPath, Enabled);
        return true;
    }
}
