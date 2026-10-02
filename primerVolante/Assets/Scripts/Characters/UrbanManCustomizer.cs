using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Characters
{
    /// <summary>
    /// Personalización modular del personaje Urban Man (ALSTRA INFINITE): cambio de torso y de accesorios.
    /// Torso: las mallas de otro prefab (Separated/) se reasignan a los huesos de ESTE esqueleto por nombre,
    /// de modo que se animan con el personaje; el esqueleto sobrante del prefab se destruye.
    /// Accesorios: mallas rígidas que se anclan al hueso de la cabeza. Los accesorios que comparten
    /// "slot" son excluyentes entre sí (ej. anteojos y anteojos de sol).
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

        [Serializable]
        public class AccessoryOption
        {
            [Tooltip("Identificador usado desde código (ej. \"Cap\").")]
            [SerializeField] private string m_Id;

            [Tooltip("Zona del cuerpo. Dos accesorios del mismo slot no pueden estar equipados a la vez.")]
            [SerializeField] private string m_Slot;

            [Tooltip("Prefab Accessories/ (malla rígida, sin esqueleto).")]
            [SerializeField] private GameObject m_Prefab;

            [Tooltip("Posición del accesorio en el espacio del personaje (raíz del prefab) con el personaje en pose de reposo. Los accesorios del asset NO están modelados en el espacio del hueso, sino en posiciones absolutas.")]
            [SerializeField] private Vector3 m_RestPosition = Vector3.zero;

            [Tooltip("Rotación (grados) del accesorio respecto de la raíz del personaje en pose de reposo. Cero = orientado igual que el personaje.")]
            [SerializeField] private Vector3 m_RestEulerAngles = Vector3.zero;

            public string Id => m_Id;
            public string Slot => m_Slot;
            public GameObject Prefab => m_Prefab;
            public Vector3 RestPosition => m_RestPosition;
            public Vector3 RestEulerAngles => m_RestEulerAngles;
        }

        [Header("Esqueleto")]
        [Tooltip("Hueso raíz del esqueleto del personaje (el hijo \"root\" del prefab). Define el destino del reasignado de huesos.")]
        [SerializeField] private Transform m_RootBone;

        [Tooltip("Nombre del hueso de la cabeza donde se anclan los accesorios.")]
        [SerializeField] private string m_HeadBoneName = "head.x";

        [Header("Torso")]
        [Tooltip("Mallas del torso que trae el personaje de fábrica. Se ocultan mientras haya un torso alternativo equipado.")]
        [SerializeField] private SkinnedMeshRenderer[] m_DefaultTorsoRenderers = Array.Empty<SkinnedMeshRenderer>();

        [Tooltip("Torsos alternativos disponibles.")]
        [SerializeField] private List<TorsoOption> m_TorsoOptions = new List<TorsoOption>();

        [Tooltip("Torso equipado al iniciar. Vacío = el torso de fábrica.")]
        [SerializeField] private string m_InitialTorsoId;

        [Header("Accesorios")]
        [Tooltip("Accesorios disponibles.")]
        [SerializeField] private List<AccessoryOption> m_AccessoryOptions = new List<AccessoryOption>();

        [Tooltip("Accesorios equipados al iniciar.")]
        [SerializeField] private List<string> m_InitialAccessoryIds = new List<string>();

        private readonly List<GameObject> m_TorsoInstances = new List<GameObject>();
        private readonly Dictionary<string, GameObject> m_EquippedAccessories = new Dictionary<string, GameObject>();
        private Dictionary<string, Transform> m_BoneMap;
        private string m_CurrentTorsoId;

        /// <summary>Se dispara cada vez que cambia el torso o algún accesorio.</summary>
        public event Action Changed;

        /// <summary>Id del torso alternativo equipado, o null si se usa el de fábrica.</summary>
        public string CurrentTorsoId => m_CurrentTorsoId;

        public IReadOnlyList<TorsoOption> TorsoOptions => m_TorsoOptions;

        public IReadOnlyList<AccessoryOption> AccessoryOptions => m_AccessoryOptions;

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

            foreach (string id in m_InitialAccessoryIds)
            {
                EquipAccessory(id);
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

        /// <summary>Equipa el accesorio; si otro del mismo slot estaba puesto, lo reemplaza.</summary>
        public bool EquipAccessory(string id)
        {
            AccessoryOption option = m_AccessoryOptions.Find(o => o.Id == id);
            if (option == null || option.Prefab == null)
            {
                Debug.LogWarning($"[UrbanManCustomizer] No existe el accesorio '{id}'.", this);
                return false;
            }

            if (m_EquippedAccessories.ContainsKey(id))
            {
                return true;
            }

            if (!EnsureBoneMap() || !m_BoneMap.TryGetValue(m_HeadBoneName, out Transform head))
            {
                Debug.LogWarning($"[UrbanManCustomizer] No se encontró el hueso '{m_HeadBoneName}'.", this);
                return false;
            }

            UnequipSlot(option.Slot);

            GameObject instance = Instantiate(option.Prefab, head, false);
            instance.name = option.Prefab.name;
            ApplyRestPose(instance.transform, head, option);
            m_EquippedAccessories[id] = instance;
            Changed?.Invoke();
            return true;
        }

        public bool UnequipAccessory(string id)
        {
            if (!m_EquippedAccessories.TryGetValue(id, out GameObject instance))
            {
                return false;
            }

            m_EquippedAccessories.Remove(id);
            Dispose(instance);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Quita todo lo que haya equipado en ese slot.</summary>
        public void UnequipSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot))
            {
                return;
            }

            var toRemove = new List<string>();
            foreach (string equippedId in m_EquippedAccessories.Keys)
            {
                AccessoryOption equipped = m_AccessoryOptions.Find(o => o.Id == equippedId);
                if (equipped != null && string.Equals(equipped.Slot, slot, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(equippedId);
                }
            }

            foreach (string equippedId in toRemove)
            {
                UnequipAccessory(equippedId);
            }
        }

        /// <summary>Alterna el accesorio. Devuelve true si quedó equipado.</summary>
        public bool ToggleAccessory(string id)
        {
            if (IsAccessoryEquipped(id))
            {
                UnequipAccessory(id);
                return false;
            }

            return EquipAccessory(id);
        }

        public bool IsAccessoryEquipped(string id) => m_EquippedAccessories.ContainsKey(id);

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

        /// <summary>
        /// Coloca el accesorio donde le corresponde con el personaje en pose de reposo. Usa la bind pose del hueso
        /// (que no cambia con la animación) para pasar de espacio del personaje a espacio local del hueso.
        /// </summary>
        private void ApplyRestPose(Transform accessory, Transform head, AccessoryOption option)
        {
            foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int index = Array.IndexOf(smr.bones, head);
                if (index < 0)
                {
                    continue;
                }

                Matrix4x4 smrInCharacter = transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                Matrix4x4 boneRestInCharacter = smrInCharacter * smr.sharedMesh.bindposes[index].inverse;
                Matrix4x4 accessoryInCharacter = Matrix4x4.TRS(option.RestPosition, Quaternion.Euler(option.RestEulerAngles), Vector3.one);
                Matrix4x4 local = boneRestInCharacter.inverse * accessoryInCharacter;

                accessory.localPosition = local.GetPosition();
                accessory.localRotation = local.rotation;
                return;
            }

            Debug.LogWarning($"[UrbanManCustomizer] Ninguna malla del personaje usa el hueso '{head.name}': el accesorio queda sin ajuste de pose.", this);
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
