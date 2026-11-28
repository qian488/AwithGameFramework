using BBads.Quality;
using UnityEngine;
using UnityEngine.UI;

namespace BBads
{
    [RequireComponent(typeof(Image))]
    public sealed class BBadsBattleArt : MonoBehaviour
    {
        void Awake()
        {
            Image background = GetComponent<Image>();
            background.sprite = BBadsArtQuality.LoadSprite(BBadsArtQuality.BackgroundKey, background.sprite);
        }
    }
}
