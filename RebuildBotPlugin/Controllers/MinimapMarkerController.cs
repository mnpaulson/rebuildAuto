using UnityEngine;
using UnityEngine.UI;

namespace RebuildBotPlugin.Controllers
{
    public class MinimapMarkerController
    {
        private GameObject minimapMarkerObject = null;
        private RectTransform minimapMarkerRect = null;
        private Image minimapMarkerImage = null;
        private static Sprite markerSprite = null;

        private static Sprite CreateWaypointMarkerSprite()
        {
            int size = 28;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Color clear = new Color(0, 0, 0, 0);
            Color cyan = new Color(0f, 0.95f, 1f, 1f); // Neon Cyan outer ring
            Color gold = new Color(1f, 0.85f, 0.1f, 1f); // Bright Gold center

            Vector2 center = new Vector2((size - 1) / 2f, (size - 1) / 2f);
            float radius = (size - 4) / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);

                    // Outer ring (width ~1.2px)
                    if (Mathf.Abs(dist - radius) <= 1.2f)
                    {
                        tex.SetPixel(x, y, cyan);
                    }
                    // Crosshair tick marks
                    else if ((Mathf.Abs(x - center.x) <= 0.8f || Mathf.Abs(y - center.y) <= 0.8f) && dist <= radius + 2f && dist >= 3f)
                    {
                        tex.SetPixel(x, y, cyan);
                    }
                    // Center bullseye
                    else if (dist <= 2.5f)
                    {
                        tex.SetPixel(x, y, gold);
                    }
                    else
                    {
                        tex.SetPixel(x, y, clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        public void UpdateWaypointMarker(bool isBotEnabled, bool isAutoWander, BotState currentState, Vector2Int currentWaypoint)
        {
            var controller = Assets.Scripts.UI.Hud.MinimapController.Instance;
            if (controller == null || controller.MapImage == null || controller.MapImage.sprite == null)
            {
                if (minimapMarkerObject != null && minimapMarkerObject.activeSelf)
                    minimapMarkerObject.SetActive(false);
                return;
            }

            bool shouldShow = isBotEnabled &&
                             isAutoWander &&
                             currentState == BotState.Wandering &&
                             currentWaypoint != Vector2Int.zero;

            if (shouldShow)
            {
                if (minimapMarkerObject == null)
                {
                    minimapMarkerObject = new GameObject("WanderWaypointMarker");
                    minimapMarkerObject.transform.SetParent(controller.MapImage.transform, false);

                    minimapMarkerRect = minimapMarkerObject.AddComponent<RectTransform>();
                    minimapMarkerRect.anchorMin = Vector2.zero;
                    minimapMarkerRect.anchorMax = Vector2.zero;
                    minimapMarkerRect.sizeDelta = new Vector2(22, 22);

                    minimapMarkerImage = minimapMarkerObject.AddComponent<Image>();
                    markerSprite ??= CreateWaypointMarkerSprite();
                    minimapMarkerImage.sprite = markerSprite;
                }

                var h = controller.MapImage.sprite.texture.height;
                var offset = new Vector3(0.5f, 0.5f, 0);
                minimapMarkerRect.localPosition = new Vector3(
                    currentWaypoint.x * controller.MinimapPixelsPerTile / 2f,
                    currentWaypoint.y * controller.MinimapPixelsPerTile / 2f - h,
                    0f) + offset;

                if (!minimapMarkerObject.activeSelf)
                    minimapMarkerObject.SetActive(true);
            }
            else
            {
                if (minimapMarkerObject != null && minimapMarkerObject.activeSelf)
                    minimapMarkerObject.SetActive(false);
            }
        }
    }
}
