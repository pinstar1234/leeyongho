using UnityEngine;
using UnityEngine.UI;
using TMPro;
using InfraTown.Economy;
using InfraTown.Power;
using InfraTown.Gas;
using InfraTown.Population;
using DG.Tweening;

namespace InfraTown.UI
{
    public class TopHUDManager : MonoBehaviour
    {
        [Header("자금 (Money) UI")]
        [SerializeField] private TextMeshProUGUI moneyText;
        [SerializeField] private RectTransform moneyContainer;

        [Header("전력 (Power) UI")]
        [SerializeField] private TextMeshProUGUI powerText;
        [SerializeField] private RectTransform powerContainer;
        [SerializeField] private Color normalPowerColor = Color.white;
        [SerializeField] private Color shortagePowerColor = new Color(1f, 0.35f, 0.35f, 1f);
        [SerializeField] private TextMeshProUGUI gasText;

        [Header("인구 (Population) UI")]
        [SerializeField] private TextMeshProUGUI populationText;
        [SerializeField] private RectTransform populationContainer;
        [SerializeField] private Color normalPopulationColor = Color.white;
        [SerializeField] private Color maxPopulationColor = new Color(1f, 0.75f, 0.25f, 1f);

        [Header("대시보드 연동 버튼 (선택 사항)")]
        [SerializeField] private Button overviewButton;
        [SerializeField] private OverviewUI overviewUI;

        private int _displayedMoney;
        private int _lastPopulation = -1;
        private Tween _moneyCountTween;
        private GasManager _gas;

        private void Start()
        {
            if (overviewButton != null && overviewUI != null)
            {
                overviewButton.onClick.AddListener(overviewUI.ShowOverview);
            }

            RegisterEvents();
            RefreshInitialData();

            _gas = GasManager.Instance;
            if (gasText != null && _gas != null)
            {
                _gas.OnGasDataChanged += HandleGasChanged;
                HandleGasChanged(_gas.CurrentUsedGas, _gas.CurrentAvailableGas);
            }
        }

        private void OnDestroy()
        {
            UnregisterEvents();
            if (_gas != null) _gas.OnGasDataChanged -= HandleGasChanged;
            _moneyCountTween?.Kill();
        }

        private void RegisterEvents()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged += HandleMoneyChanged;
            }

            if (PowerManager.Instance != null)
            {
                PowerManager.Instance.OnPowerDataChanged += HandlePowerChanged;
            }

            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.OnPopulationChanged += HandlePopulationChanged;
            }
        }

        private void UnregisterEvents()
        {
            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.OnMoneyChanged -= HandleMoneyChanged;
            }

            if (PowerManager.Instance != null)
            {
                PowerManager.Instance.OnPowerDataChanged -= HandlePowerChanged;
            }

            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.OnPopulationChanged -= HandlePopulationChanged;
            }
        }

        private void RefreshInitialData()
        {
            if (EconomyManager.Instance != null)
            {
                _displayedMoney = EconomyManager.Instance.CurrentMoney;
                if (moneyText != null)
                {
                    moneyText.text = $"{_displayedMoney:N0} 원";
                }
            }

            if (PowerManager.Instance != null)
            {
                HandlePowerChanged(
                    PowerManager.Instance.CurrentUsedPower,
                    PowerManager.Instance.CurrentAvailablePower
                );
            }

            if (PopulationManager.Instance != null)
            {
                HandlePopulationChanged(
                    PopulationManager.Instance.CurrentPopulation,
                    PopulationManager.Instance.MaxPopulationCapacity
                );
            }
        }

        private void HandleMoneyChanged(int currentBalance, int changeAmount)
        {
            if (moneyText == null) return;

            _moneyCountTween?.Kill();

            int startValue = _displayedMoney;
            _moneyCountTween = DOVirtual.Int(startValue, currentBalance, 0.35f, value =>
            {
                _displayedMoney = value;
                moneyText.text = $"{_displayedMoney:N0} 원";
            }).SetEase(Ease.OutCubic).SetUpdate(true);

            if (moneyContainer != null && changeAmount != 0)
            {
                moneyContainer.DOKill();
                moneyContainer.localScale = Vector3.one;
                moneyContainer.DOPunchScale(Vector3.one * 0.12f, 0.25f, 8, 1f).SetUpdate(true);
            }
        }

        private void HandlePowerChanged(int usedPower, int availablePower)
        {
            if (powerText == null) return;

            powerText.text = $"{usedPower} / {availablePower} kW";

            bool isShortage = usedPower > availablePower;
            powerText.color = isShortage ? shortagePowerColor : normalPowerColor;

            if (isShortage && powerContainer != null)
            {
                powerContainer.DOKill();
                powerContainer.localScale = Vector3.one;
                powerContainer.DOPunchScale(Vector3.one * 0.15f, 0.3f, 10, 1f).SetUpdate(true);
            }
        }

        private void HandlePopulationChanged(int currentPop, int maxCapacity)
        {
            if (populationText == null) return;

            populationText.text = $"{currentPop} / {maxCapacity} 명";

            bool isFull = maxCapacity > 0 && currentPop >= maxCapacity;
            populationText.color = isFull ? maxPopulationColor : normalPopulationColor;

            if (_lastPopulation != -1 && _lastPopulation != currentPop && populationContainer != null)
            {
                populationContainer.DOKill();
                populationContainer.localScale = Vector3.one;
                populationContainer.DOPunchScale(Vector3.one * 0.12f, 0.25f, 8, 1f).SetUpdate(true);
            }

            _lastPopulation = currentPop;
        }

        private void HandleGasChanged(int used, int available)
        {
            gasText.text = $"{used:N0} / {available:N0}";
            gasText.color = used > available ? shortagePowerColor : normalPowerColor;
        }
    }
}
