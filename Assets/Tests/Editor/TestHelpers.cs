using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using InfraTown.Building;
using InfraTown.Core;
using InfraTown.Economy;
using InfraTown.Population;
using InfraTown.Power;
using InfraTown.Satisfaction;
using InfraTown.Services;
using InfraTown.TimeSystem;

namespace InfraTown.Tests
{
    /// <summary>
    /// EditMode 테스트 공용 도우미.
    /// EditMode에서는 Awake/Start/OnDestroy가 자동 호출되지 않으므로 리플렉션으로 직접 호출하고,
    /// 테스트가 끝나면 생성한 오브젝트와 싱글톤 Instance를 정리한다.
    /// </summary>
    public class TestWorld : IDisposable
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Type[] SingletonTypes =
        {
            typeof(GameLoopManager), typeof(EconomyManager), typeof(PopulationManager),
            typeof(PowerManager), typeof(SatisfactionManager), typeof(ServiceAccessibilityManager),
            typeof(TimeManager), typeof(BuildingManager), typeof(GameOutcomeManager)
        };

        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets = new List<ScriptableObject>();
        private readonly float _originalTimeScale;

        public TestWorld()
        {
            _originalTimeScale = Time.timeScale;
            ResetSingletons();
        }

        /// <summary>매니저 생성 + Awake 호출 (callStart=true면 Start도 호출)</summary>
        public T CreateManager<T>(bool callStart = false) where T : MonoBehaviour
        {
            var go = new GameObject(typeof(T).Name);
            _objects.Add(go);
            var comp = go.AddComponent<T>();
            Invoke(comp, "Awake");
            if (callStart) Invoke(comp, "Start");
            return comp;
        }

        public BuildingData CreateData(
            BuildingType category,
            string id = "test",
            string name = "테스트 건물",
            BuildingSizeType size = BuildingSizeType.Small,
            int power = 0,
            int maintenance = 0,
            int construction = 0,
            int capacity = 0,
            float impact = 0f,
            int radius = 3,
            int requiredPopulation = 0)
        {
            var data = ScriptableObject.CreateInstance<BuildingData>();
            _assets.Add(data);
            data.buildingID = id;
            data.buildingName = name;
            data.category = category;
            data.sizeType = size;
            data.requiredPower = power;
            data.maintenanceCost = maintenance;
            data.constructionCost = construction;
            data.housingCapacity = capacity;
            data.satisfactionImpact = impact;
            data.satisfactionRadius = radius;
            data.requiredPopulation = requiredPopulation;
            return data;
        }

        /// <summary>프리팹 없이 BuildingInstance만 가진 건물 생성 (withPole=true면 전봇대 컴포넌트 추가)</summary>
        public BuildingInstance CreateBuilding(BuildingData data, int x, int y, bool withPole = false)
        {
            var go = new GameObject(data.buildingName);
            _objects.Add(go);
            var instance = go.AddComponent<BuildingInstance>();
            instance.Init(data, new Vector2Int(x, y));
            if (withPole)
            {
                var pole = go.AddComponent<UtilityPoleInstance>();
                Invoke(pole, "Awake");
            }
            return instance;
        }

        public static object Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, AnyInstance, null, GetTypes(args), null)
                         ?? target.GetType().GetMethod(methodName, AnyInstance);
            if (method == null) throw new MissingMethodException(target.GetType().Name, methodName);
            return method.Invoke(target, args);
        }

        public static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, AnyInstance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
        }

        public static T GetField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, AnyInstance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);
            return (T)field.GetValue(target);
        }

        public void Dispose()
        {
            foreach (var go in _objects)
            {
                if (go == null) continue;
                foreach (var mb in go.GetComponents<MonoBehaviour>())
                {
                    var onDestroy = mb.GetType().GetMethod("OnDestroy", AnyInstance);
                    onDestroy?.Invoke(mb, null);
                }
                UnityEngine.Object.DestroyImmediate(go);
            }
            _objects.Clear();

            foreach (var asset in _assets)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }
            _assets.Clear();

            ResetSingletons();
            Time.timeScale = _originalTimeScale;
        }

        private static void ResetSingletons()
        {
            foreach (var type in SingletonTypes)
            {
                var prop = type.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
                prop?.SetValue(null, null);
            }
        }

        private static Type[] GetTypes(object[] args)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == null) return Type.EmptyTypes;
                types[i] = args[i].GetType();
            }
            return types;
        }
    }
}
