using UnityEngine;

namespace InfraTown.Grid
{
    public class TileController : MonoBehaviour
    {
        [Header("Grid Position")]
        [SerializeField] private int gridX;
        [SerializeField] private int gridZ;

        public int GridX => gridX;
        public int GridZ => gridZ;

        private MeshRenderer _meshRenderer;
        private MaterialPropertyBlock _propBlock;
        private static readonly int ColorPropertyID = Shader.PropertyToID("_BaseColor"); // URP: _BaseColor / Built-in: _Color
        private static readonly int EmissionColorPropertyID = Shader.PropertyToID("_EmissionColor");

        private Color _originalColor;
        private bool _isHighlighted = false;

        private void Awake()
        {
            _meshRenderer = GetComponent<MeshRenderer>();
            _propBlock = new MaterialPropertyBlock();

            if (_meshRenderer != null && _meshRenderer.sharedMaterial != null)
            {
                _originalColor = _meshRenderer.sharedMaterial.color;
            }
        }

        public void Init(int x, int z)
        {
            gridX = x;
            gridZ = z;
            gameObject.name = $"Tile_{x}_{z}";
        }

        /// <summary>
        /// Keyword Enable 및 PropertyBlock을 활용한 명확한 발광(Emission) 처리
        /// </summary>
        public void SetHighlight(bool active, Color highlightColor, float emissionIntensity = 2.0f)
        {
            if (_isHighlighted == active || _meshRenderer == null) return;
            _isHighlighted = active;

            _meshRenderer.GetPropertyBlock(_propBlock);

            if (active)
            {
                // URP 및 Standard Shader 발광 Keyword 활성화
                _meshRenderer.material.EnableKeyword("_EMISSION");

                _propBlock.SetColor(ColorPropertyID, highlightColor);
                // HDR Emission Color 설정 (강도 곱셈)
                _propBlock.SetColor(EmissionColorPropertyID, highlightColor * emissionIntensity);
            }
            else
            {
                _meshRenderer.material.DisableKeyword("_EMISSION");
                _propBlock.SetColor(ColorPropertyID, _originalColor);
                _propBlock.SetColor(EmissionColorPropertyID, Color.black);
            }

            _meshRenderer.SetPropertyBlock(_propBlock);
        }
    }
}