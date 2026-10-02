using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Characters
{
    /// <summary>
    /// Personalización modular del personaje Urban Man (ALSTRA INFINITE): cambio de torso.
    /// Torso: las mallas de otro prefab (Separated/) se reasignan a los huesos de ESTE esqueleto por nombre,
    /// de modo que se animan con el personaje; el esqueleto sobrante del prefab se destruye.
    /// </summary>
    public class UrbanManCustomizer : MonoBehaviour
    {
        [Serializable]
        public class TorsoOption
        {
            [Tooltip("Identificador usado desde código (ej. \"HoodieUp\").")]
            [SerializeField] private string m_Id;

            [Tooltip("Prefab Separated/ con las mallas del torso y su propio esqueleto.")]
            [SerializeField] private GameObject m_Prefab;

            public string Id => m_Id;
            public GameObject Prefab => m_Prefab;
        }

        [Header("Esqueleto")]
        [Tooltip("Hueso raíz del esqueleto del personaje (el hijo \"root\" del prefab). Define el destino del reasignado de huesos.")]
        [SerializeField] private Transform m_RootBone;
        [Header("Torso")]
        [Tooltip("Mallas del torso que trae el personaje de fábrica. Se ocultan mientras haya un torso alternativo equipado.")]
        [SerializeField] private SkinnedMeshRenderer[] m_DefaultTorsoRenderers = Array.Empty<SkinnedMeshRenderer>();

        [Tooltip("Torsos alternativos disponibles.")]
        [SerializeField] private List<TorsoOption> m_TorsoOptions = new List<TorsoOption>();

        [Tooltip("Torso equipado al iniciar. Vacío = el torso de fábrica.")]
        [SerializeField] private string m_InitialTorsoId;

        private readonly List<GameObject> m_TorsoInstances = new List<GameObject>();
        private Dictionary<string, Transform> m_BoneMap;
        private string m_CurrentTorsoId;

        /// <summary>Se dispara cada vez que cambia el torso.</summary>
        public event Action Changed;

        /// <summary>Id del torso alternativo equipado, o null si se usa el de fábrica.</summary>
        public string CurrentTorsoId => m_CurrentTorsoId;

        public IReadOnlyList<TorsoOption> TorsoOptions => m_TorsoOptions;

        private void Reset()
        {
            m_RootBone = transform.Find("root");

            var defaults = new List<SkinnedMeshRenderer>();
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.name.StartsWith("Torso_", StringComparison.Ordinal))
                {
                    defaults.Add(smr);
                }
            }
            m_DefaultTorsoRenderers = defaults.ToArray();
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(m_InitialTorsoId))
            {
                EquipTorso(m_InitialTorsoId);
            }
        }

        /// <summary>Equipa el torso alternativo con ese id. Devuelve false si no existe o no se pudo montar.</summary>
        public bool EquipTorso(string id)
        {
            TorsoOption option = m_TorsoOptions.Find(o => o.Id == id);
            if (option == null || option.Prefab == null)
            {
                Debug.LogWarning($"[UrbanManCustomizer] No existe el torso '{id}'.", this);
                return false;
            }

            if (m_CurrentTorsoId == id)
            {
                return true;
            }

            if (!EnsureBoneMap())
            {
                return false;
            }

            GameObject part = Instantiate(option.Prefab);
            SkinnedMeshRenderer[] renderers = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[UrbanManCustomizer] El prefab '{option.Prefab.name}' no tiene SkinnedMeshRenderer.", this);
                Dispose(part);
                return false;
            }

            ClearCustomTorso();

            foreach (SkinnedMeshRenderer smr in renderers)
            {
                RebindToSkeleton(smr);
                smr.transform.SetParent(transform, false);
                m_TorsoInstances.Add(smr.gameObject);
            }

            // Las mallas ya se movieron: lo que queda es el esqueleto duplicado del prefab.
            Dispose(part);

            SetDefaultTorsoActive(false);
            m_CurrentTorsoId = id;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Vuelve al torso de fábrica.</summary>
        public void EquipDefaultTorso()
        {
            if (m_CurrentTorsoId == null)
            {
                return;
            }

            ClearCustomTorso();
            SetDefaultTorsoActive(true);
            m_CurrentTorsoId = null;
            Changed?.Invoke();
        }

        private bool EnsureBoneMap()
        {
            if (m_BoneMap != null)
            {
                return true;
            }

            if (m_RootBone == null)
            {
                Debug.LogError("[UrbanManCustomizer] Falta asignar el hueso raíz (m_RootBone).", this);
                return false;
            }

            m_BoneMap = new Dictionary<string, Transform>();
            foreach (Transform bone in m_RootBone.GetComponentsInChildren<Transform>(true))
            {
                m_BoneMap.TryAdd(bone.name, bone);
            }

            return true;
        }

        private void RebindToSkeleton(SkinnedMeshRenderer smr)
        {
            Transform[] source = smr.bones;
            var target = new Transform[source.Length];
            var missing = new List<string>();

            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                {
                    continue;
                }

                if (!m_BoneMap.TryGetValue(source[i].name, out target[i]))
                {
                    missing.Add(source[i].name);
                }
            }

            smr.bones = target;
            if (smr.rootBone != null && m_BoneMap.TryGetValue(smr.rootBone.name, out Transform rootBone))
            {
                smr.rootBone = rootBone;
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning($"[UrbanManCustomizer] '{smr.name}': {missing.Count} huesos sin equivalente en el esqueleto ({string.Join(", ", missing)}).", this);
            }
        }

        private void ClearCustomTorso()
        {
            foreach (GameObject instance in m_TorsoInstances)
            {
                Dispose(instance);
            }
            m_TorsoInstances.Clear();
        }

        private void SetDefaultTorsoActive(bool active)
        {
            foreach (SkinnedMeshRenderer smr in m_DefaultTorsoRenderers)
            {
                if (smr != null)
                {
                    smr.gameObject.SetActive(active);
                }
            }
        }

        private static void Dispose(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
