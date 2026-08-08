using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class GameClearController : MonoBehaviour
{
    [SerializeField] private CanvasGroup contentCanvasGroup;
    [SerializeField] private RectTransform titleGroup;
    [SerializeField] private TextMeshProUGUI returnPrompt;
    [SerializeField, Min(0f)] private float inputDelay = 0.75f;

    private Sequence _introSequence;
    private Tween _promptTween;
    private Tween _inputDelayTween;
    private bool _canReturnToMenu;

    private void Start()
    {
        contentCanvasGroup.alpha = 0f;
        titleGroup.localScale = new Vector3(0.65f, 1.35f, 1f);

        _introSequence = DOTween.Sequence()
            .SetUpdate(true)
            .Append(contentCanvasGroup.DOFade(1f, 0.16f))
            .Join(titleGroup.DOScale(Vector3.one, 0.42f).SetEase(Ease.OutBack))
            .Append(titleGroup.DOShakeAnchorPos(0.2f, 12f, 14, 90f, false, true));

        if (returnPrompt != null)
        {
            returnPrompt.alpha = 1f;
            _promptTween = returnPrompt.DOFade(0.18f, 0.55f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true);
        }

        _inputDelayTween = DOVirtual.DelayedCall(
                inputDelay,
                () => _canReturnToMenu = true)
            .SetUpdate(true);
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
        _introSequence?.Kill();
        _promptTween?.Kill();
        _inputDelayTween?.Kill();
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
