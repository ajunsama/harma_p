using UnityEngine;
using System.Collections;

public class EnemyVisual : MonoBehaviour
{
    [SerializeField] Color tintColor = Color.white;

    IEnumerator Start()
    {
        yield return null;
        var renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material.SetColor("_Color", tintColor);
        }
    }
}
