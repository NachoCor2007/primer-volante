using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrimerVolante.Environment
{
    /// <summary>
    /// Conjunto de dueños que suspenden un detector (<c>WrongWayDetector</c>, <c>TurnSignalDetector</c>,
    /// <c>LowBeamDetector</c>). El detector está suspendido mientras algún dueño lo tenga suspendido, así dos
    /// sistemas (por ejemplo dos pasos de lección) no se pisan. Los dueños que son <see cref="UnityEngine.Object"/> ya
    /// destruidos se descartan solos, así que un paso de lección destruido sin liberar no deja el detector
    /// suspendido para siempre.
    /// </summary>
    public sealed class SuppressionSet
    {
        // Dueños que son UnityEngine.Object y ya fueron destruidos. Sin captura: no genera basura por llamada.
        private static readonly Predicate<object> s_IsDestroyedOwner = owner => owner is UnityEngine.Object unityObject && unityObject == null;

        private readonly HashSet<object> m_Owners = new HashSet<object>();
        private readonly string m_LogPrefix;

        /// <param name="logPrefix">Prefijo del aviso cuando el dueño es null (ej. <c>"[LowBeamDetector]"</c>).</param>
        public SuppressionSet(string logPrefix)
        {
            m_LogPrefix = logPrefix;
        }

        /// <summary>true mientras algún dueño vivo tenga suspendido el detector. Descarta los dueños destruidos.</summary>
        public bool IsActive
        {
            get
            {
                if (m_Owners.Count > 0) m_Owners.RemoveWhere(s_IsDestroyedOwner);
                return m_Owners.Count > 0;
            }
        }

        /// <summary>
        /// Suspende o reanuda en nombre de <paramref name="owner"/>. Con un dueño null avisa y no hace nada.
        /// Devuelve true si la llamada se aplicó (el dueño no era null).
        /// </summary>
        public bool Set(object owner, bool suppressed)
        {
            if (owner == null)
            {
                Debug.LogWarning($"{m_LogPrefix} ⚠️ SetSuppressed requiere un dueño (this); se ignora la llamada.");
                return false;
            }

            if (suppressed) m_Owners.Add(owner);
            else m_Owners.Remove(owner);
            return true;
        }
    }
}
