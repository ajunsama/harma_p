using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds and updates the unified far/mid/near background layer stack.
/// </summary>
[DefaultExecutionOrder(450)]
public sealed class LayeredBackgroundController : MonoBehaviour
{
    private sealed class RuntimeLayer
    {
        public BackgroundLayerData data;
        public Transform root;
        public float tileWidth;
    }

    private readonly List<RuntimeLayer> runtimeLayers = new List<RuntimeLayer>();
    private Camera targetCamera;
    private Vector3 initialCameraPosition;
    private float startTime;

    public void Initialize(Camera camera, IList<BackgroundLayerData> layers)
    {
        targetCamera = camera != null ? camera : Camera.main;
        initialCameraPosition = targetCamera != null ? targetCamera.transform.position : Vector3.zero;
        startTime = Time.time;
        ClearLayers();

        if (layers == null) return;
        foreach (var layer in layers)
            if (layer != null) BuildLayer(layer);
        UpdateLayers(0f);
    }

    public static Vector2 CalculateCameraOffset(Vector2 cameraDelta, BackgroundLayerData layer)
    {
        if (layer == null) return Vector2.zero;
        return new Vector2(
            cameraDelta.x * (1f - layer.MotionMultiplierX),
            cameraDelta.y * (1f - layer.MotionMultiplierY));
    }

    public static int GetRequiredTileCount(float viewportWidth, float tileWidth)
    {
        if (tileWidth <= Mathf.Epsilon) return 0;
        int visible = Mathf.CeilToInt(Mathf.Max(0f, viewportWidth) / tileWidth);
        int count = Mathf.Max(3, visible + 2);
        return count % 2 == 0 ? count + 1 : count;
    }

    private void LateUpdate()
    {
        UpdateLayers(Mathf.Max(0f, Time.time - startTime));
    }

    private void BuildLayer(BackgroundLayerData data)
    {
        var rootObject = new GameObject(string.IsNullOrWhiteSpace(data.displayName)
            ? "BackgroundLayer"
            : data.displayName);
        rootObject.transform.SetParent(transform, false);
        var runtime = new RuntimeLayer { data = data, root = rootObject.transform };

        switch (data.contentType)
        {
            case BackgroundLayerContentType.SequentialTiles:
                BuildSequence(runtime);
                break;
            case BackgroundLayerContentType.RepeatedSprite:
                BuildRepeated(runtime);
                break;
            default:
                if (data.sprite != null)
                    CreateRenderer(runtime.root, data.sprite, Vector3.zero, data);
                break;
        }

        runtimeLayers.Add(runtime);
    }

    private void BuildRepeated(RuntimeLayer runtime)
    {
        Sprite sprite = runtime.data.sprite;
        if (sprite == null) return;
        runtime.tileWidth = sprite.bounds.size.x * Mathf.Abs(runtime.data.scale.x);
        if (runtime.tileWidth <= Mathf.Epsilon) return;

        float viewportWidth = targetCamera != null && targetCamera.orthographic
            ? targetCamera.orthographicSize * 2f * targetCamera.aspect
            : runtime.tileWidth * 3f;
        int count = GetRequiredTileCount(viewportWidth, runtime.tileWidth);
        int half = count / 2;
        for (int i = -half; i <= half; i++)
            CreateRenderer(runtime.root, sprite, new Vector3(i * runtime.tileWidth, 0f, 0f), runtime.data);
    }

    private void BuildSequence(RuntimeLayer runtime)
    {
        if (runtime.data.sequence == null) return;
        float cursor = 0f;
        foreach (var entry in runtime.data.sequence)
        {
            if (entry == null || entry.sprite == null || entry.repeatCount <= 0) continue;
            float width = entry.sprite.bounds.size.x * Mathf.Abs(runtime.data.scale.x);
            if (width <= Mathf.Epsilon) continue;
            for (int i = 0; i < entry.repeatCount; i++)
            {
                float x = cursor - entry.sprite.bounds.min.x * runtime.data.scale.x;
                float y = -entry.sprite.bounds.center.y * runtime.data.scale.y;
                CreateRenderer(runtime.root, entry.sprite, new Vector3(x, y, 0f), runtime.data);
                cursor += width;
            }
        }
    }

    private static SpriteRenderer CreateRenderer(
        Transform parent, Sprite sprite, Vector3 localPosition, BackgroundLayerData data)
    {
        var tile = new GameObject(sprite != null ? sprite.name : "BackgroundTile");
        tile.transform.SetParent(parent, false);
        tile.transform.localPosition = localPosition;
        tile.transform.localScale = new Vector3(data.scale.x, data.scale.y, 1f);
        var renderer = tile.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = data.color;
        renderer.sortingOrder = data.SortingOrder;
        return renderer;
    }

    private void UpdateLayers(float elapsed)
    {
        Vector3 cameraPosition = targetCamera != null ? targetCamera.transform.position : initialCameraPosition;
        Vector2 cameraDelta = cameraPosition - initialCameraPosition;

        foreach (var runtime in runtimeLayers)
        {
            if (runtime?.root == null || runtime.data == null) continue;
            Vector2 offset = CalculateCameraOffset(cameraDelta, runtime.data);
            float x = runtime.data.origin.x + offset.x + runtime.data.horizontalScrollSpeed * elapsed;
            float y = runtime.data.origin.y + offset.y;

            if (runtime.data.contentType == BackgroundLayerContentType.RepeatedSprite &&
                runtime.tileWidth > Mathf.Epsilon && targetCamera != null)
            {
                x = cameraPosition.x + Mathf.Repeat(
                    x - cameraPosition.x + runtime.tileWidth * 0.5f,
                    runtime.tileWidth) - runtime.tileWidth * 0.5f;
            }

            runtime.root.position = new Vector3(x, y, 10f);
        }
    }

    private void ClearLayers()
    {
        runtimeLayers.Clear();
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }
}
