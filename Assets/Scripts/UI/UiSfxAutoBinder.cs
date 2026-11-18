using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

namespace RunningGame.UI
{
    /// <summary>
    /// 运行时自动给场景里所有按钮补齐 <see cref="UiButtonSfx"/>，无需手动逐个挂载。
    /// 会覆盖未激活面板（暂停、胜利、设置等）里的按钮，以及运行时动态生成的按钮（如关卡列表 3/4/5）。
    /// </summary>
    public class UiSfxAutoBinder : MonoBehaviour
    {
        private static UiSfxAutoBinder _runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_runner != null) return;
            var go = new GameObject("UiSfxAutoBinder");
            DontDestroyOnLoad(go);
            _runner = go.AddComponent<UiSfxAutoBinder>();
        }

        private void Awake()
        {
            if (_runner != null && _runner != this)
            {
                Destroy(gameObject);
                return;
            }
            _runner = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(BindRoutine());
        }

        private void OnDestroy()
        {
            if (_runner == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            StartCoroutine(BindRoutine());
        }

        // 小键盘 8/2（NumLock 开启时）也能上下移动焦点；方向键/W/S 由 Vertical 轴处理。
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Keypad8)) MoveSelection(up: true);
            else if (Input.GetKeyDown(KeyCode.Keypad2)) MoveSelection(up: false);
        }

        private static void MoveSelection(bool up)
        {
            var es = EventSystem.current;
            if (es == null) return;
            var current = es.currentSelectedGameObject;
            if (current == null) return;

            var selectable = current.GetComponent<Selectable>();
            if (selectable == null) return;

            var next = up ? selectable.FindSelectableOnUp() : selectable.FindSelectableOnDown();
            if (next != null && next != selectable)
                es.SetSelectedGameObject(next.gameObject);
        }

        // 场景加载后分几帧反复扫描，确保运行时生成的按钮也能被覆盖。
        private IEnumerator BindRoutine()
        {
            BindAll();
            for (int i = 0; i < 5; i++)
            {
                yield return null;
                BindAll();
            }
            yield return new WaitForSecondsRealtime(0.5f);
            BindAll();
            yield return new WaitForSecondsRealtime(1.0f);
            BindAll();
        }

        private static void BindAll()
        {
            var buttons = FindObjectsOfType<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null) continue;
                if (button.GetComponent<UiButtonSfx>() == null)
                    button.gameObject.AddComponent<UiButtonSfx>();
            }
        }
    }
}
