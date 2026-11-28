using System;
using UnityEngine;

namespace AwithGameFrame.Unity.Presentation
{
    public sealed class UnityPrototypeView : MonoBehaviour
    {
        [Serializable]
        public struct Node
        {
            public string id;
            public string binding;
            public string[] pages;
            public GameObject target;
        }

        [SerializeField] private Node[] nodes;
        [SerializeField, HideInInspector] private string source;

        public Node[] Nodes => nodes;
        public string Source => source;

        public void ShowPage(string page)
        {
            foreach (var node in nodes)
            {
                bool visible = node.pages == null || node.pages.Length == 0 || Array.IndexOf(node.pages, page) >= 0;
                if (node.target.activeSelf != visible) node.target.SetActive(visible);
            }
        }

        public T Get<T>(string binding) where T : Component
        {
            foreach (var node in nodes)
                if (node.binding == binding && node.target.TryGetComponent<T>(out var component)) return component;
            throw new InvalidOperationException($"Prototype binding '{binding}' requires {typeof(T).Name}.");
        }

#if UNITY_EDITOR
        public void SetGeneratedNodes(Node[] generatedNodes, string sourceJson)
        {
            nodes = generatedNodes;
            source = sourceJson;
        }
#endif
    }
}
