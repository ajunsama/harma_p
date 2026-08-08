using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class ArcadeButtonHover : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    ISelectHandler,
    IDeselectHandler
{
    [SerializeField] private RectTransform target;
    [SerializeField] private float selectedScale = 1.045f;
    [SerializeField] private float duration = 0.1f;

    private bool _pointerInside;
    private bool _selected;

    private void Awake()
    {
        if (target == null)
            target = transform as RectTransform;
    }

    private void OnDisable()
    {
        if (target == null)
            return;

        target.DOKill();
        target.localScale = Vector3.one;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _pointerInside = true;
        Animate();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerInside = false;
        Animate();
    }

    public void OnSelect(BaseEventData eventData)
    {
        _selected = true;
        Animate();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        _selected = false;
        Animate();
    }

    private void Animate()
    {
        if (target == null)
            return;

        float scale = _pointerInside || _selected ? selectedScale : 1f;
        target.DOKill();
        target.DOScale(scale, duration).SetEase(Ease.OutQuad).SetUpdate(true);
    }
}
