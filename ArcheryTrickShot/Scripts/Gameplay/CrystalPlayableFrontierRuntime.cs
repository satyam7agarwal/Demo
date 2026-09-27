using System.Reflection;
using TMPro;
using UnityEngine;

/// <summary>
/// Prevents the temporary highest-authored Crystal Caverns level from being
/// presented as the end of the entire game while later chapter levels are
/// still in production.
///
/// LevelData.IsGameFinale is the authoritative opt-in for a true campaign
/// finale, so this remains data-driven as more levels are added.
/// </summary>
[DisallowMultipleComponent]
public sealed class CrystalPlayableFrontierRuntime : MonoBehaviour
{
    private LevelManager levelManager;
    private GameUIController gameUI;

    private static FieldInfo currentLevelField;
    private static FieldInfo viewField;

    private void Awake()
    {
        levelManager =
            GetComponent<LevelManager>();

        currentLevelField ??=
            typeof(LevelManager).GetField(
                "currentLevel",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        viewField ??=
            typeof(GameUIController).GetField(
                "view",
                BindingFlags.Instance |
                BindingFlags.NonPublic);
    }

    private void LateUpdate()
    {
        if (levelManager == null ||
            currentLevelField == null ||
            viewField == null)
        {
            return;
        }

        LevelData level =
            currentLevelField.GetValue(
                levelManager) as LevelData;

        if (level == null ||
            level.IsGameFinale ||
            level.LevelNumber < 11 ||
            level.LevelNumber > 20)
        {
            return;
        }

        if (gameUI == null)
        {
            gameUI =
                Object.FindFirstObjectByType<GameUIController>();
        }

        if (gameUI == null)
            return;

        GameUIView view =
            viewField.GetValue(
                gameUI) as GameUIView;

        if (view == null ||
            view.ResultTitle == null)
        {
            return;
        }

        // GameUIController currently equates "last loaded LevelData" with
        // "campaign finale". During staged world production those are not the
        // same thing. Correct only that temporary frontier state.
        if (view.ResultTitle.text !=
            "ALL LEVELS COMPLETE!")
        {
            return;
        }

        view.ResultTitle.text =
            "CRYSTAL TRIAL COMPLETE!";

        if (view.ResultPrimaryButtonText != null)
        {
            view.ResultPrimaryButtonText.text =
                "BACK TO MAP  >>";
        }

        if (view.ResultReplayButton != null)
        {
            view.ResultReplayButton.gameObject.SetActive(true);
        }
    }
}
