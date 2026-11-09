using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 총 프리팹(Prefabs/Pistol)의 임시 모양(상자·원기둥)을 Reichsrevolver M1879 모델로 바꾼다.
/// 에셋 스토어 패키지(Assets/Reichsrevolver_M1879)를 먼저 가져와야 한다. 패키지 재질은 Built-in Standard라서 URP Lit 재질을 새로 만든다.
/// 모델 치수(총구, 실린더, 그립 위치)는 메시에서 잰 값이다. 모델을 바꾸면 다시 잰다.
/// </summary>
public static class RevolverModelSetup
{
    const string k_PistolPath = "Assets/_Project/Prefabs/Pistol.prefab";
    const string k_ModelPrefabPath = "Assets/Reichsrevolver_M1879/Prefabs/ReichsrevolverM1879.prefab";
    const string k_TexturePath = "Assets/Reichsrevolver_M1879/Textures/";
    const string k_RevolverMaterialPath = "Assets/_Project/Art/Material/Revolver_M1879.mat";
    const string k_ShellMaterialPath = "Assets/_Project/Art/Material/Revolver_Shell.mat";

    // 모델 루트 기준 치수 (+Z가 총구 쪽, +Y가 위, -X가 왼쪽)
    static readonly Vector3 k_MuzzlePoint = new Vector3(0f, 0.068f, 0.155f);
    static readonly Vector3 k_GripPoint = new Vector3(0f, -0.045f, -0.125f);
    // 실린더가 왼쪽 아래로 젖혀지도록 크레인 축을 실린더 왼쪽 아래에 둔다
    static readonly Vector3 k_CranePivot = new Vector3(-0.022f, 0.03f, -0.027f);

    [MenuItem("BombDefuse/Set Up Revolver Model")]
    static void SetUpFromMenu()
    {
        var summary = SetUp();
        EditorUtility.DisplayDialog("Set Up Revolver Model", summary, "OK");
    }

    /// <summary>대화상자 없이 실행하고 결과 요약을 돌려준다.</summary>
    public static string SetUp()
    {
        var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_ModelPrefabPath);
        if (modelPrefab == null)
            return $"Import the Reichsrevolver M-1879 package first ({k_ModelPrefabPath} not found).";

        var log = new List<string>();
        var revolverMaterial = GetOrCreateMaterial(k_RevolverMaterialPath, "ReichsrevolverM1879", "1024", log);
        var shellMaterial = GetOrCreateMaterial(k_ShellMaterialPath, "ReichsrevolverM1879Shell", "256", log);

        var root = PrefabUtility.LoadPrefabContents(k_PistolPath);
        try
        {
            if (root.transform.Find("Model") == null)
                BuildModel(root, modelPrefab, revolverMaterial, shellMaterial, log);
            RemoveLoadZone(root, log);
            SetGrabColliders(root, log);
            PrefabUtility.SaveAsPrefabAsset(root, k_PistolPath);
            log.Add($"Saved {k_PistolPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var summary = string.Join("\n", log);
        Debug.Log($"[RevolverModelSetup]\n{summary}");
        return summary;
    }

    static void BuildModel(GameObject root, GameObject modelPrefab, Material revolverMaterial, Material shellMaterial, List<string> log)
    {
        var pistol = root.transform;
        var attach = pistol.Find("AttachPoint");

        // 임시 모양(상자·원기둥)과 그 콜라이더를 지운다
        foreach (var placeholder in new[] { "Cube", "Cylinder" })
        {
            var child = pistol.Find(placeholder);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
                log.Add($"Removed placeholder {placeholder}");
            }
        }

        // 패키지 프리팹을 풀어서 넣는다 (패키지의 Animator와 데모 스크립트는 뼈를 덮어쓰므로 뺀다)
        var model = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, pistol);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        foreach (var component in model.GetComponents<MonoBehaviour>())
            Object.DestroyImmediate(component);
        Object.DestroyImmediate(model.GetComponent<Animator>());

        // 모델의 +Z(총구)가 총의 발사 방향(AttachPoint의 앞)을 보게 하고, 그립이 AttachPoint에 오게 둔다
        var modelTransform = model.transform;
        var attachRotation = attach != null ? attach.localRotation : Quaternion.Euler(0f, 270f, 0f);
        var attachPosition = attach != null ? attach.localPosition : Vector3.zero;
        modelTransform.localRotation = attachRotation;
        modelTransform.localScale = Vector3.one;
        modelTransform.localPosition = attachPosition - attachRotation * k_GripPoint;

        foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            skinned.sharedMaterial = revolverMaterial;
        foreach (var meshRenderer in model.GetComponentsInChildren<MeshRenderer>(true))
            meshRenderer.sharedMaterial = shellMaterial;

        var bones = model.GetComponentsInChildren<Transform>(true);
        var cylinder = bones.First(t => t.name == "Cylinder");
        var hammer = bones.First(t => t.name == "Hammer");
        var shells = cylinder.Cast<Transform>().Where(t => t.name.StartsWith("ReichsrevolverM1879Shell")).Select(t => t.gameObject).ToArray();

        var revolverModel = model.AddComponent<RevolverModel>();
        var modelSerialized = new SerializedObject(revolverModel);
        modelSerialized.FindProperty("m_Cylinder").objectReferenceValue = cylinder;
        modelSerialized.FindProperty("m_Hammer").objectReferenceValue = hammer;
        var shellsProperty = modelSerialized.FindProperty("m_Shells");
        shellsProperty.arraySize = shells.Length;
        for (var i = 0; i < shells.Length; i++)
            shellsProperty.GetArrayElementAtIndex(i).objectReferenceValue = shells[i];
        var body = cylinder.parent;
        modelSerialized.FindProperty("m_CranePivot").vector3Value = body.InverseTransformPoint(modelTransform.TransformPoint(k_CranePivot));
        modelSerialized.FindProperty("m_CraneAxis").vector3Value = body.InverseTransformDirection(modelTransform.forward).normalized;
        modelSerialized.ApplyModifiedPropertiesWithoutUndo();

        // 잡기·충돌용 콜라이더: 총열과 몸통, 그립
        AddBox(modelTransform, "Collider_Frame", new Vector3(0f, 0.057f, 0.022f), new Vector3(0.03f, 0.055f, 0.265f));
        AddBox(modelTransform, "Collider_Grip", new Vector3(0f, -0.045f, -0.13f), new Vector3(0.03f, 0.08f, 0.05f));

        // 총구를 모델의 총구 끝에 맞춘다 (방향은 그대로)
        var muzzle = pistol.Find("Muzzle");
        if (muzzle != null)
            muzzle.position = modelTransform.TransformPoint(k_MuzzlePoint);

        var gun = root.GetComponent<Gun>();
        var gunSerialized = new SerializedObject(gun);
        gunSerialized.FindProperty("m_Model").objectReferenceValue = revolverModel;
        gunSerialized.ApplyModifiedPropertiesWithoutUndo();

        log.Add("Added Reichsrevolver model, colliders, RevolverModel; moved Muzzle");
    }

    // 여분 총알 넣기를 뺐으므로 장전 영역도 지운다
    static void RemoveLoadZone(GameObject root, List<string> log)
    {
        var loadZone = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "LoadZone");
        if (loadZone == null)
            return;

        Object.DestroyImmediate(loadZone.gameObject);
        log.Add("Removed LoadZone");
    }

    // 잡기는 XRGrabInteractable의 Colliders 목록으로 판정한다. 임시 모양의 콜라이더를 지웠으므로 새 콜라이더로 바꾼다
    static void SetGrabColliders(GameObject root, List<string> log)
    {
        var grab = root.GetComponent<XRGrabInteractable>();
        var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToArray();
        var serialized = new SerializedObject(grab);
        var list = serialized.FindProperty("m_Colliders");
        var same = list.arraySize == colliders.Length;
        for (var i = 0; same && i < colliders.Length; i++)
            same = list.GetArrayElementAtIndex(i).objectReferenceValue == colliders[i];
        if (same)
            return;

        list.arraySize = colliders.Length;
        for (var i = 0; i < colliders.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        log.Add($"Grab colliders: {string.Join(", ", colliders.Select(c => c.name))}");
    }

    static void AddBox(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var box = go.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    // 패키지의 Standard(Specular) 재질과 같은 텍스처로 URP Lit(Specular) 재질을 만든다
    static Material GetOrCreateMaterial(string path, string texturePrefix, string size, List<string> log)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
            return material;

        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Texture Load(string kind) => AssetDatabase.LoadAssetAtPath<Texture>($"{k_TexturePath}{texturePrefix}_{kind}_{size}.tga");

        material.SetFloat("_WorkflowMode", 0f);
        material.EnableKeyword("_SPECULAR_SETUP");
        material.SetTexture("_BaseMap", Load("diffuse"));
        material.SetColor("_BaseColor", Color.white);

        var specular = Load("specular");
        material.SetTexture("_SpecGlossMap", specular);
        material.SetColor("_SpecColor", new Color(0.2f, 0.2f, 0.2f));
        if (specular != null)
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        material.SetFloat("_Smoothness", 0.5f);

        var normal = Load("normal");
        material.SetTexture("_BumpMap", normal);
        if (normal != null)
            material.EnableKeyword("_NORMALMAP");

        var occlusion = Load("ao");
        material.SetTexture("_OcclusionMap", occlusion);
        if (occlusion != null)
            material.EnableKeyword("_OCCLUSIONMAP");

        AssetDatabase.CreateAsset(material, path);
        log.Add($"Created {path}");
        return material;
    }
}
