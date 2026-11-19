using System;
using UnityEngine;

namespace AwithGameFrame.Unity.Editor
{
    [Serializable]
    internal class AutomationEdit
    {
        public string action;
        public string asset;
        public string target;
        public string parent;
        public string name;
        public string kind;
        public string query;
        public float[] position;
        public float[] rotation;
        public float[] scale;
        public int limit;
        public bool preview;
        public AutomationPropertyEdit[] properties;
    }

    [Serializable]
    internal sealed class AutomationRequest : AutomationEdit
    {
        public int version;
        public string id;
        public bool batch;
        public long expiresUtc;
        public string scene;
        public bool save;
        public AutomationEdit[] steps;
        public string snapshot;
        public string expectedHash;
    }

    [Serializable]
    internal sealed class AutomationResponse
    {
        public int version = 1;
        public string id;
        public bool ok;
        public string error;
        public string unityVersion;
        public string projectPath;
        public string scene;
        public bool dirty;
        public bool truncated;
        public AutomationObject[] objects;
        public string[] assets;
        public AutomationProperty[] properties;
        public bool preview;
        public string outcome;
        public string action;
        public string completedUtc;
        public bool batch;
        public AutomationStep[] steps;
        public AutomationCheckpoint checkpoint;
        public AutomationCheckpoint[] checkpoints;
    }

    [Serializable]
    internal sealed class AutomationStep
    {
        public string action;
        public string target;
        public string asset;
        public string kind;
        public string outcome;
        public string error;
        public AutomationObject before;
        public AutomationObject[] objects;
        public AutomationProperty[] properties;
    }

    [Serializable]
    internal sealed class AutomationCheckpoint
    {
        public string id;
        public string asset;
        public string guid;
        public string savedHash;
        public string currentHash;
        public string createdUtc;
    }

    [Serializable]
    internal sealed class AutomationPropertyEdit
    {
        public string path;
        public string value;
    }

    [Serializable]
    internal sealed class AutomationProperty
    {
        public string path;
        public string type;
        public string value;
        public string previousValue;
        public bool writable;
    }

    [Serializable]
    internal sealed class AutomationObject
    {
        public string path;
        public bool active;
        public Vector3 localPosition;
        public Vector3 localRotation;
        public Vector3 localScale;
        public string[] components;
    }
}
