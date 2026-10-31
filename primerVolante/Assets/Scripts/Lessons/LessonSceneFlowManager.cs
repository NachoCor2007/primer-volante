using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using PrimerVolante.VR;

namespace PrimerVolante.Lessons
{
    /// <summary>
    /// Orquestador de flujo de escenas aditivas del simulador "Primer Volante".
    /// Administra la escena persistente base de la ciudad (City_Master) y realiza transiciones
    /// limpias e higiénicas en Realidad Virtual (Fade Out -> Unload overlay -> Load nuevo overlay -> Fade In)
    /// entre el Menú Principal y las diferentes lecciones del currículum.
    /// Cumple estrictamente la regla de cero APIs obsoletas (AGENTS.md).
    /// </summary>
    [DisallowMultipleComponent]
    public class LessonSceneFlowManager : MonoBehaviour
    {
        private static LessonSceneFlowManager s_Instance;

        public const string CITY_MASTER_SCENE = "City_Master";
        public const string MAIN_MENU_SCENE = "MainMenu";

        public static readonly Dictionary<string, string> LessonScenes = new Dictionary<string, string>
        {
            { "LESSON_01", "Lesson_01_BasicDriving" },
            { "LESSON_02", "Lesson_02_EngineTransmission" },
            { "LESSON_03", "Lesson_03_LightingSignals" },
            { "LESSON_04", "Lesson_04_DrivingManeuvers" },
            { "LESSON_05", "Lesson_05_TurnIntersections" },
            { "LESSON_06", "Lesson_06_ParkingManeuvers" }
        };

        private string m_CurrentOverlayScene = "";
        private bool m_IsTransitioning = false;

        public static LessonSceneFlowManager Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = UnityEngine.Object.FindAnyObjectByType<LessonSceneFlowManager>();
                    if (s_Instance == null)
                    {
                        var go = new GameObject("LessonSceneFlowManager");
                        s_Instance = go.AddComponent<LessonSceneFlowManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return s_Instance;
            }
        }

        public bool IsTransitioning => m_IsTransitioning;
        public string CurrentOverlayScene => m_CurrentOverlayScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnRuntimeInit()
        {
            var mgr = Instance;
            if (mgr != null)
            {
                mgr.DetectActiveOverlayScene();
                mgr.EnsureCityMasterLoaded();
            }
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            DetectActiveOverlayScene();
            EnsureCityMasterLoaded();
        }

        /// <summary>
        /// Detecta cuál es la escena overlay activa actualmente cargada (MainMenu o alguna Lesson).
        /// </summary>
        public void DetectActiveOverlayScene()
        {
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.name != CITY_MASTER_SCENE)
                {
                    m_CurrentOverlayScene = s.name;
                    break;
                }
            }
        }

        /// <summary>
        /// Garantiza que City_Master esté cargada de forma aditiva en segundo plano si aún no lo está.
        /// </summary>
        public void EnsureCityMasterLoaded(Action onComplete = null)
        {
            Scene cityScene = SceneManager.GetSceneByName(CITY_MASTER_SCENE);
            if (!cityScene.isLoaded)
            {
                StartCoroutine(LoadCityMasterRoutine(onComplete));
            }
            else
            {
                onComplete?.Invoke();
            }
        }

        private IEnumerator LoadCityMasterRoutine(Action onComplete)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(CITY_MASTER_SCENE, LoadSceneMode.Additive);
            if (op != null)
            {
                while (!op.isDone) yield return null;
            }
            onComplete?.Invoke();
        }

        /// <summary>
        /// Carga una lección por su LessonDefinitionSO.
        /// </summary>
        public void LoadLesson(LessonDefinitionSO lesson)
        {
            if (lesson == null) return;
            LoadLesson(lesson.lessonId);
        }

        /// <summary>
        /// Carga una lección por su ID ("LESSON_01", etc.).
        /// </summary>
        public void LoadLesson(string lessonId)
        {
            if (m_IsTransitioning) return;

            if (LessonScenes.TryGetValue(lessonId, out string targetScene))
            {
                StartCoroutine(TransitionToSceneRoutine(targetScene));
            }
            else
            {
                Debug.LogWarning($"[LessonSceneFlowManager] No hay escena mapeada para {lessonId}.");
            }
        }

        /// <summary>
        /// Vuelve a la escena del Menú Principal.
        /// </summary>
        public void ReturnToMainMenu()
        {
            if (m_IsTransitioning) return;
            StartCoroutine(TransitionToSceneRoutine(MAIN_MENU_SCENE));
        }

        /// <summary>
        /// Carga la siguiente lección en el currículum si existe; de lo contrario regresa al Menú.
        /// </summary>
        public void LoadNextLesson(LessonDefinitionSO currentLesson)
        {
            if (currentLesson == null)
            {
                ReturnToMainMenu();
                return;
            }

            // Buscar siguiente lección
            int currNum = 1;
            if (currentLesson.lessonId.StartsWith("LESSON_") && int.TryParse(currentLesson.lessonId.Substring(7), out int parsedNum))
            {
                currNum = parsedNum;
            }

            int nextNum = currNum + 1;
            string nextId = $"LESSON_{nextNum:D2}";

            if (LessonScenes.ContainsKey(nextId))
            {
                LoadLesson(nextId);
            }
            else
            {
                ReturnToMainMenu();
            }
        }

        /// <summary>
        /// Reinicia la escena overlay de la lección actual.
        /// </summary>
        public void RestartCurrentLesson()
        {
            if (m_IsTransitioning) return;
            if (!string.IsNullOrEmpty(m_CurrentOverlayScene) && m_CurrentOverlayScene != MAIN_MENU_SCENE)
            {
                StartCoroutine(TransitionToSceneRoutine(m_CurrentOverlayScene));
            }
        }

        private IEnumerator TransitionToSceneRoutine(string targetSceneName)
        {
            m_IsTransitioning = true;

            // 1. Fade Out en VR
            VRScreenFader fader = VRScreenFader.Instance ?? UnityEngine.Object.FindAnyObjectByType<VRScreenFader>();
            if (fader != null)
            {
                yield return fader.FadeOutRoutine(0.35f);
            }
            else
            {
                yield return new WaitForSeconds(0.2f);
            }

            // 2. Descargar escena overlay actual (si existe y no es la ciudad)
            DetectActiveOverlayScene();
            if (!string.IsNullOrEmpty(m_CurrentOverlayScene) && m_CurrentOverlayScene != CITY_MASTER_SCENE)
            {
                Scene currentScene = SceneManager.GetSceneByName(m_CurrentOverlayScene);
                if (currentScene.isLoaded)
                {
                    AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(currentScene);
                    if (unloadOp != null)
                    {
                        while (!unloadOp.isDone) yield return null;
                    }
                }
            }

            // 3. Asegurar que City_Master esté cargada
            Scene cityScene = SceneManager.GetSceneByName(CITY_MASTER_SCENE);
            if (!cityScene.isLoaded)
            {
                AsyncOperation loadCityOp = SceneManager.LoadSceneAsync(CITY_MASTER_SCENE, LoadSceneMode.Additive);
                if (loadCityOp != null)
                {
                    while (!loadCityOp.isDone) yield return null;
                }
            }

            // 4. Cargar nueva escena overlay
            AsyncOperation loadTargetOp = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Additive);
            if (loadTargetOp != null)
            {
                while (!loadTargetOp.isDone) yield return null;
            }

            m_CurrentOverlayScene = targetSceneName;
            Scene newScene = SceneManager.GetSceneByName(targetSceneName);
            if (newScene.isLoaded)
            {
                SceneManager.SetActiveScene(newScene);
            }

            // 5. Pequeño frame de asentamiento de físicas y tracking
            yield return new WaitForEndOfFrame();

            // 6. Fade In en VR
            fader = VRScreenFader.Instance ?? UnityEngine.Object.FindAnyObjectByType<VRScreenFader>();
            if (fader != null)
            {
                yield return fader.FadeInRoutine(0.35f);
            }

            m_IsTransitioning = false;
            Debug.Log($"[LessonSceneFlowManager] Transición completada hacia escena: {targetSceneName}");
        }
    }
}
