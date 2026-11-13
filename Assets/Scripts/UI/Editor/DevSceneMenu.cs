// 메뉴: BlackWhale > D 개발용 태블릿 UI 씬 만들기
// 빈 씬에 DevTabletBootstrap 하나만 붙여 Assets/Scenes/DevTabletUI.unity로 저장한다.
// 공통 Game 씬은 A와 시점을 맞춰 편집하므로, 이 개발용 씬은 따로 둔다.
using BlackWhale.UI.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlackWhale.UI.EditorTools
{
    public static class DevSceneMenu
    {
        private const string ScenePath = "Assets/Scenes/DevTabletUI.unity";

        [MenuItem("BlackWhale/D 개발용 태블릿 UI 씬 만들기")]
        public static void CreateDevScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (System.IO.File.Exists(ScenePath))
            {
                bool overwrite = EditorUtility.DisplayDialog("개발용 씬", ScenePath + " 이(가) 이미 있습니다. 새로 만들까요?", "새로 만들기", "열기만");
                if (!overwrite)
                {
                    EditorSceneManager.OpenScene(ScenePath);
                    return;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("DevTabletBootstrap");
            go.AddComponent<DevTabletBootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = go;
            Debug.Log("[BlackWhale D] " + ScenePath + " 생성 완료. Play를 누르면 A GameRoom + 대역 서비스로 화면이 실행됩니다.");
        }
    }
}
