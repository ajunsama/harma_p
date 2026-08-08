using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

public sealed class GameOverController : MonoBehaviour
{
    [SerializeField] private Button restartButton;
    [SerializeField] private CanvasGroup contentCanvasGroup;
    [SerializeField] private RectTransform titleGroup;
    [SerializeField] private TextMeshProUGUI returnPrompt;
    [SerializeField, Min(0f)] private float inputDelay = 0.75f;

    private Sequence _introSequence;
    private Tween _promptTween;
    private Tween _inputDelayTween;
    private bool _canReturnToMenu;

    private void Awake()
    {
        restartButton.onClick.AddListener(RestartLevel);
    }

    private void Start()
    {
        PlayIntro();
        _inputDelayTween = DOVirtual.DelayedCall(
                inputDelay,
                () => _canReturnToMenu = true)
            .SetUpdate(true);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
    }

    private void Update()
    {
        if (!_canReturnToMenu || !WasAnyReturnButtonPressed())
            return;

        _canReturnToMenu = false;
        GameFlowService.ReturnToMainMenu();
    }

    private void OnDestroy()
    {
        restartButton.onClick.RemoveListener(RestartLevel);
        _introSequence?.Kill();
        _promptTween?.Kill();
        _inputDelayTween?.Kill();
    }

    private void PlayIntro()
    {
        contentCanvasGroup.alpha = 0f;
        titleGroup.anchoredPosition += new Vector2(0f, 130f);
        titleGroup.localScale = new Vector3(1.3f, 0.65f, 1f);

        _introSequence = DOTween.Sequence()
            .SetUpdate(true)
            .Append(contentCanvasGroup.DOFade(1f, 0.12f))
            .Join(titleGroup.DOAnchorPosY(120f, 0.32f).SetEase(Ease.OutBounce))
            .Join(titleGroup.DOScale(Vector3.one, 0.28f).SetEase(Ease.OutBack))
            .Append(titleGroup.DOShakeAnchorPos(0.28f, 24f, 24, 90f, false, true));

        if (returnPrompt != null)
        {
            returnPrompt.alpha = 1f;
            _promptTween = returnPrompt.DOFade(0.18f, 0.55f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }
    }

    private void RestartLevel()
    {
        _canReturnToMenu = false;
        GameFlowService.RestartLastGameplayScene();
    }

    private static bool WasAnyReturnButtonPressed()
    {
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            return true;

        if (Gamepad.current == null)
            return false;

        foreach (var control in Gamepad.current.allControls)
        {
            if (control is ButtonControl button && button.wasPressedThisFrame)
                return true;
        }

        return false;
    }
}
