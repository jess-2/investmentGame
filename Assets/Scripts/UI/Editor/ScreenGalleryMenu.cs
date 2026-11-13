// 메뉴: BlackWhale > D 화면 미리보기 (게임 미연결)
// 저장하지 않은 새 빈 씬에 ScreenGallery 하나만 놓고 Play를 시작한다. 기존 씬·공통 Game 씬은 건드리지 않는다.
using BlackWhale.UI.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlackWhale.UI.EditorTools
{
    public static class ScreenGalleryMenu
    {
        [MenuItem("BlackWhale/D 화면 미리보기 (게임 미연결)")]
        public static void OpenGallery()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.Log("[BlackWhale D] Play를 멈춘 뒤 다시 실행하세요.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("ScreenGallery");
            go.AddComponent<ScreenGallery>();
            Selection.activeGameObject = go;
            Debug.Log("[BlackWhale D] 화면 미리보기를 시작합니다. 이 씬은 저장하지 않아도 됩니다.");
            EditorApplication.EnterPlaymode();
        }
    }
}
