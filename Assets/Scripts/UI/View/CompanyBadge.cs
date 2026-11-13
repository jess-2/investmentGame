// NEXT-D03 기업 이미지 표시. Resources/CompanyLogos/<ID>.png가 있으면 그 이미지를, 없으면 색 배지+글자로 대체한다.
// 이미지가 없어도 예외 없이 동작하며 선택·구매를 막지 않는다.
using System.Collections.Generic;
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public static class CompanyBadge
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>이미지 스프라이트. 에셋이 없으면 null(대체 표시 사용).</summary>
        public static Sprite LogoOf(CompanyEntry entry)
        {
            if (entry?.LogoResource == null) return null;
            if (Cache.TryGetValue(entry.LogoResource, out var cached)) return cached;
            Sprite sprite = null;
            var tex = Resources.Load<Texture2D>(entry.LogoResource);
            if (tex != null)
            {
                sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = "Logo_" + entry.Id;
            }
            Cache[entry.LogoResource] = sprite;
            return sprite;
        }

        public static Color ColorOf(CompanyEntry entry)
            => ColorUtility.TryParseHtmlString("#" + (entry?.ColorHex ?? "6B7280"), out var c) ? c : Color.gray;

        /// <summary>parent 안에 size 크기의 로고(또는 대체 배지)를 만든다. 반환: 로고 RectTransform.</summary>
        public static RectTransform Create(Transform parent, string companyId, float size, bool dimmed = false)
        {
            var entry = CompanyCatalog.Get(companyId);
            var logo = LogoOf(entry);
            var frame = UiKit.Panel(parent, "Logo_" + entry.Id, logo != null ? Color.white : ColorOf(entry), rounded: true);
            frame.rectTransform.sizeDelta = new Vector2(size, size);

            if (logo != null)
            {
                var img = UiKit.Panel(frame.transform, "Image", Color.white);
                img.sprite = logo;
                img.preserveAspect = true;
                UiKit.Stretch(img.rectTransform, 2, 2, 2, 2);
                if (dimmed) img.color = new Color(1, 1, 1, 0.45f);
            }
            else
            {
                var t = UiKit.Label(frame.transform, entry.Badge, Mathf.RoundToInt(size * 0.38f), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Stretch(t.rectTransform);
                if (dimmed) frame.color = new Color(frame.color.r, frame.color.g, frame.color.b, 0.45f);
            }
            return frame.rectTransform;
        }
    }
}
