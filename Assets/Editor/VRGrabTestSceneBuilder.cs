using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// VR 연결 테스트용 씬 생성기.
/// 메뉴: Tools > VR Test > Build Grab Test Scene
/// XR Origin(Starter Assets) + 바닥 + 테이블 + 잡을 수 있는 박스 3개로 구성된 씬을 만든다.
/// </summary>
public static class VRGrabTestSceneBuilder
{
    const string XriPackage = "com.unity.xr.interaction.toolkit";
    const string StarterAssetsSample = "Starter Assets";
    const string XROriginPrefabName = "XR Origin (XR Rig)";
    const string ScenePath = "Assets/Scenes/VRGrabTest.unity";

    [MenuItem("Tools/VR Test/Build Grab Test Scene")]
    public static void Build()
    {
        var originPrefab = FindXROriginPrefab();
        if (originPrefab == null)
        {
            if (!ImportStarterAssets())
                return;
            // 샘플 import 후 AssetDatabase 갱신이 끝나야 프리팹을 찾을 수 있다.
            EditorUtility.DisplayDialog("VR Test",
                "XR Interaction Toolkit Starter Assets를 가져왔습니다.\n" +
                "컴파일이 끝난 뒤 메뉴를 한 번 더 실행해 주세요.", "확인");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 기본 Main Camera는 XR Origin 안의 카메라와 겹치므로 제거.
        var defaultCamera = Object.FindFirstObjectByType<Camera>();
        if (defaultCamera != null)
            Object.DestroyImmediate(defaultCamera.gameObject);

        var origin = (GameObject)PrefabUtility.InstantiatePrefab(originPrefab);
        origin.transform.position = Vector3.zero;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.localScale = new Vector3(2f, 1f, 2f);
        SetColor(floor, new Color(0.35f, 0.35f, 0.38f));

        var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Table";
        table.transform.position = new Vector3(0f, 0.4f, 0.8f);
        table.transform.localScale = new Vector3(1.2f, 0.8f, 0.6f);
        SetColor(table, new Color(0.55f, 0.4f, 0.25f));

        CreateGrabBox("Grab Box Red", new Vector3(-0.35f, 0.9f, 0.8f), Color.red);
        CreateGrabBox("Grab Box Green", new Vector3(0f, 0.9f, 0.8f), Color.green);
        CreateGrabBox("Grab Box Blue", new Vector3(0.35f, 0.9f, 0.8f), new Color(0.2f, 0.4f, 1f));

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
        Debug.Log($"[VR Test] 씬 생성 완료: {ScenePath}");
    }

    static GameObject CreateGrabBox(string name, Vector3 position, Color color)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = Vector3.one * 0.15f;
        SetColor(box, color);

        var body = box.AddComponent<Rigidbody>();
        body.mass = 0.5f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var grab = box.AddComponent<XRGrabInteractable>();
        grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
        grab.throwOnDetach = true;
        return box;
    }

    static void SetColor(GameObject go, Color color)
    {
        var renderer = go.GetComponent<Renderer>();
        // 에디터에서 sharedMaterial을 직접 바꾸면 기본 머티리얼이 오염되므로 새 머티리얼을 만든다.
        var material = new Material(renderer.sharedMaterial) { color = color };
        renderer.sharedMaterial = material;
    }

    static GameObject FindXROriginPrefab()
    {
        return AssetDatabase.FindAssets($"\"{XROriginPrefabName}\" t:Prefab")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.Contains(StarterAssetsSample))
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .FirstOrDefault(prefab => prefab != null && prefab.name == XROriginPrefabName);
    }

    static bool ImportStarterAssets()
    {
        var sample = Sample.FindByPackage(XriPackage, null)
            .FirstOrDefault(s => s.displayName == StarterAssetsSample);
        if (string.IsNullOrEmpty(sample.displayName))
        {
            EditorUtility.DisplayDialog("VR Test",
                "XR Interaction Toolkit 패키지의 Starter Assets 샘플을 찾을 수 없습니다.\n" +
                "Package Manager에서 XR Interaction Toolkit 설치 여부를 확인해 주세요.", "확인");
            return false;
        }
        return sample.Import(Sample.ImportOptions.OverridePreviousImports);
    }

    static void AddSceneToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == path))
            return;
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
