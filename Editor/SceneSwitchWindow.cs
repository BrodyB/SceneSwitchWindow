using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ToolWindows
{
	public sealed class SceneSwitchWindow : EditorWindow
	{
		#region Constants
		private const string KEY_SCENESWITCHER = "SceneSwitchWindow";
		private const int MAX_SCENES_RECENTS = 30;		// Max scenes allowed in the Recents tab
		private const int MAX_SCENES_FAVORITES = 10;	// Max scenes allowed in the Favorites tab
		private const int MAX_SCENES_BUILD = 30;		// Max scenes allowed in the Build Scenes tab
		#endregion
		
		#region Enums
		/// <summary>
		/// Enum for controlling which scenes tab is open
		/// </summary>
		private enum ScenesMode
		{
			Recent = 0,
			BuildSettings = 1,
			Favorites = 2
		}
		#endregion
		
		#region Data Classes
		/// <summary>Holds info for each scene the user has opened up in the editor</summary>
		[System.Serializable]
		public class SceneInfo
		{
			public bool enabled = true;	// Is this scene enabled / can be opened?
			public string name;			// The scene's display name
			public string scenePath;	// The scene's actual path
		}
		#endregion
		
		#region Members
		/// <summary>List of recently-opened scenes</summary>
		private List<SceneInfo> recentScenes = new List<SceneInfo>();
		/// <summary>List of favorited scenes</summary>
		private List<SceneInfo> favScenes = new List<SceneInfo>();
		/// <summary>List of scenes in the Build Settings</summary>
		private List<SceneInfo> buildScenes = new List<SceneInfo>();
		
		private ReorderableList recentList;
		private ReorderableList favList;
		private ReorderableList buildList;
		
		/// <summary>Currently active tab</summary>
		private ScenesMode scenesMode = ScenesMode.Recent;
		/// <summary>Color used for dark backgrounded entries</summary>
		private Color darkBgColor;
		/// <summary>Current scroll position in current tab</summary>
		private Vector2 scrollPosition;
		/// <summary>The last scene index that was clicked</summary>
		private int lastSelectedIndex;
		/// <summary>The last time an entry was clicked (for double-click detection)</summary>
		private double lastSelectedTime;
		#endregion
		
		#region Editor Window Methods
		[MenuItem("Window/Scene Switch Window", false, 1)]
		private static void Open()
		{
			SceneSwitchWindow window = GetWindow<SceneSwitchWindow>();
			window.titleContent = new GUIContent("Scene Switcher");
			window.Show();
		}

		private void OnEnable ()
		{
			EditorSceneManager.sceneOpened += OnSceneOpened;

			darkBgColor = EditorGUIUtility.isProSkin ?
				new Color(0.17f, 0.17f, 0.17f) :
				new Color(0.65f, 0.65f, 0.65f);

			LoadData();
			CheckAllScenesAreValid();
			
			recentList = new ReorderableList(recentScenes, typeof(SceneInfo), false, false, false, false)
			{
				drawElementCallback = (rect, i, b, focused) => DrawEntry(rect, recentScenes, i),
				onSelectCallback = list => SelectEntry(recentScenes, list.index)
			};

			favList = new ReorderableList(favScenes, typeof(SceneInfo), true, false, true, false)
			 {
				 drawElementCallback = (rect, i, b, focused) => DrawEntry(rect, favScenes, i),
				 onReorderCallback = list => SaveData(),
				 onSelectCallback = list => SelectEntry(favScenes, list.index),
				 onAddCallback = list =>
				 {
					 Scene activeScene = EditorSceneManager.GetActiveScene();
					 if (favScenes.Count < MAX_SCENES_FAVORITES)
						AddSceneToList(activeScene.name, activeScene.path, favScenes);
				 }
			 };

			buildList = new ReorderableList(buildScenes, typeof(SceneInfo), false, false, false, false)
			{
				drawElementCallback = (rect, i, b, focused) => DrawEntry(rect, buildScenes, i, false),
				onSelectCallback = list => SelectEntry(buildScenes, list.index)
			};
		}

		private void OnDisable ()
		{
			SaveData();
			
			if (recentList != null)
			{
				recentList.drawElementCallback = null;
				recentList.onSelectCallback = null;
				recentList = null;
			}
			
			if (favList != null)
			{
				favList.drawElementCallback = null;
				favList.onReorderCallback = null;
				favList.onSelectCallback = null;
				favList = null;
			}

			if (buildList != null)
			{
				buildList.drawElementCallback = null;
				buildList.onSelectCallback = null;
				buildList = null;
			}
			
			EditorSceneManager.sceneOpened -= OnSceneOpened;
		}

		public void OnGUI ()
		{
			EditorGUILayout.BeginHorizontal();

			GUI.enabled = scenesMode != ScenesMode.Recent;
			if (GUILayout.Button(new GUIContent("Recent Scenes", "Scenes you've recently opened")))
			{
				scenesMode = ScenesMode.Recent;
				scrollPosition = Vector2.zero;
				SaveData();
			}
			
			GUI.enabled = scenesMode != ScenesMode.Favorites;
			if (GUILayout.Button(new GUIContent("★ Favorites", "Scenes you've marked as favorites")))
			{
				scenesMode = ScenesMode.Favorites;
				scrollPosition = Vector2.zero;
				SaveData();
			}

			GUI.enabled = scenesMode != ScenesMode.BuildSettings;
			if (GUILayout.Button(new GUIContent("Build Settings", "Scenes listed in the Build Settings")))
			{
				scenesMode = ScenesMode.BuildSettings;
				scrollPosition = Vector2.zero;
				SaveData();
			}
			GUI.enabled = true;

			EditorGUILayout.EndHorizontal();
			GUILayout.Space(4f);

			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
			
			switch (scenesMode)
			{
				case ScenesMode.Recent:
					recentList?.DoLayoutList();
					break;

				case ScenesMode.Favorites:
					favList?.DoLayoutList();
					break;
				
				case ScenesMode.BuildSettings:
					RefreshBuildScenes();
					if (buildScenes.Count < EditorBuildSettings.scenes.Length)
					{
						EditorGUILayout.HelpBox("This list has been truncated so as to not slow down the editor!", MessageType.Warning);
						GUILayout.Space(8f);
					}
					buildList?.DoLayoutList();

					break;
			}
			
			EditorGUILayout.EndScrollView();
		}
		#endregion
		
		#region Scene Switch Methods
		/// <summary>Save all the recent scenes data to Editor Prefs</summary>
		private void SaveData()
		{
			string data = JsonUtility.ToJson(this, true);
			EditorPrefs.SetString(KEY_SCENESWITCHER, data);
		}

		/// <summary>Load all the previously-saved recent scenes data from Editor Prefs</summary>
		private void LoadData()
		{
			JsonUtility.FromJsonOverwrite(
				EditorPrefs.GetString(KEY_SCENESWITCHER, JsonUtility.ToJson(this, false)),
				this
			);
		}

		/// <summary>
		/// Update the cached versions of the Build Setting Scenes with latest
		/// </summary>
		private void RefreshBuildScenes ()
		{
			int sceneCount = Mathf.Min(EditorBuildSettings.scenes.Length, MAX_SCENES_BUILD);

			// If the internal list of Build Scenes is out of sync with the Editor Build Settings
			// rebuild the list from scratch.
			if (sceneCount != buildScenes.Count)
			{
				buildScenes.Clear();
				for (int i = 0; i < sceneCount; ++i)
				{
					SceneInfo info = new SceneInfo();
					buildScenes.Add(info);
				}
			}
			
			// Make sure cached info matches up with the Editor Build Settings
			int buildIndex = 0;
			for (int i = 0; i < sceneCount; ++i)
			{
				if (buildScenes[i] == null)
					continue;

				SceneInfo cached = buildScenes[i];
				EditorBuildSettingsScene buildScene = EditorBuildSettings.scenes[i];
				cached.enabled = buildScene.enabled;
				
				if (cached.scenePath != buildScene.path)
				{
					cached.scenePath = buildScene.path;

					string shownIndex = cached.enabled ? buildIndex.ToString() : "X";
					string sceneName = Path.GetFileName(buildScene.path);
					cached.name = $"{shownIndex}. {sceneName.Substring(0, sceneName.LastIndexOf('.'))}";
				}

				if (cached.enabled)
					++buildIndex;
			}
		}
		#endregion

		#region Helper Methods
		/// <summary>
		/// Draw a scene entry in the window
		/// </summary>
		private void DrawEntry (Rect rect, List<SceneInfo> list, int index, bool canDelete = true)
		{
			if (list is not { Count: > 0 })
				return;

			if (index < 0 || index >= list.Count || list[index] == null)
				return;
			
			SceneInfo info = list[index];
			
			if (index % 2 != 0)
				EditorGUI.DrawRect(rect, darkBgColor);

			Rect deleteRect = new Rect(rect.x, rect.y + 2f, 0f, rect.height - 4f);
			
			if (canDelete)
			{
				deleteRect.x = rect.x + 2f;
				deleteRect.width = rect.height - 4f;
				
				if (GUI.Button(deleteRect, new GUIContent("x", "Delete from list")))
				{
					list.RemoveAt(index);
					SaveData();
					return;
				}
			}

			float openButtonWidth = 72f;
			float plusButtonWidth = 24f;
			GUI.enabled = info.enabled;
			Rect nameRect = new Rect(
				deleteRect.x + deleteRect.width + 4f, 
				deleteRect.y,
				rect.width - deleteRect.width - openButtonWidth - plusButtonWidth - 10f, 
				deleteRect.height);
			EditorGUI.LabelField(nameRect, new GUIContent(info.name, info.scenePath));

			GUI.enabled = info.enabled && !Application.isPlaying;
			
			Rect openRect = new Rect(nameRect.x + nameRect.width, nameRect.y, openButtonWidth, nameRect.height);
			if (GUI.Button(openRect, new GUIContent("Open", "Open scene by itself")))
			{
				EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
				EditorSceneManager.OpenScene(info.scenePath, OpenSceneMode.Single);
			}

			GUI.enabled = !Application.isPlaying;
			Rect plusRect = new Rect(openRect.x + openRect.width + 2f, openRect.y, plusButtonWidth, openRect.height);
			if (GUI.Button(plusRect, new GUIContent("+", "Open scene additively")))
			{
				EditorSceneManager.OpenScene(info.scenePath, OpenSceneMode.Additive);
			}

			GUI.enabled = true;
		}

		/// <summary>Selects the scene asset for a list entry if double-clicked</summary>
		/// <param name="list">List of SceneInfos to go through</param>
		/// <param name="index">Index of the entry to select</param>
		private void SelectEntry (List<SceneInfo> list, int index)
		{
			if (list is not { Count: > 0 } || index < 0 || index >= list.Count)
				return;
			
			if (index == lastSelectedIndex && EditorApplication.timeSinceStartup - lastSelectedTime < 0.4)
			{
				SceneInfo selected = list[index];

				if (selected != null)
				{
					var asset = AssetDatabase.LoadMainAssetAtPath(selected.scenePath);

					if (asset != null)
					{
						Selection.activeObject = asset;
						EditorGUIUtility.PingObject(asset);
					}
				}
			}

			lastSelectedIndex = index;
			lastSelectedTime = EditorApplication.timeSinceStartup;
		}
		
		/// <summary>
		/// Add the given scene to the Recent Scenes list
		/// </summary>
		/// <param name="sceneName">Name of the scene</param>
		/// <param name="path">Path to the scene asset</param>
		/// <param name="list">List to add the entry for</param>
		/// <param name="maxListSize">Maximum number of scenes allowed in list</param>
		private void AddSceneToList(string sceneName, string path, List<SceneInfo> list, int maxListSize = 100)
		{
			if (list == null)
				return;

			SceneInfo sceneInfo = list.Find(x => x.scenePath == path);

			if (sceneInfo != null)
			{
				list.Remove(sceneInfo);
			}
			else if (list.Count >= maxListSize)
			{
				list.RemoveAt(list.Count - 1);
			}

			list.Insert(0, new SceneInfo() {
				scenePath = path,
				name = sceneName
			});

			SaveData();
		}

		/// <summary>
		/// Go through all the scene lists and remove any that no longer exist
		/// </summary>
		private void CheckAllScenesAreValid ()
		{
			for (int i = 0; i < recentScenes.Count; i++)
			{
				SceneInfo info = recentScenes[i];

				if (!File.Exists(info.scenePath))
				{
					Debug.LogWarning($"Scene Switch Window: Removing {info.name} from Recent Scenes. It no longer exists!");
					recentScenes.RemoveAt(i--);
				}
			}
			
			for (int i = 0; i < favScenes.Count; i++)
			{
				SceneInfo info = favScenes[i];

				if (!File.Exists(info.scenePath))
				{
					Debug.LogWarning($"Scene Switch Window: Removing {info.name} from Favorite Scenes. It no longer exists!");
					favScenes.RemoveAt(i--);
				}
			}
		}
		#endregion
		
		#region Event Handlers
		/// <summary>
		/// Handles the event when the user opens a scene in the Unity editor
		/// </summary>
		/// <param name="scene">Reference to the scene that was just opened</param>
		/// <param name="mode">How the scene was opened</param>
		private void OnSceneOpened (Scene scene, OpenSceneMode mode)
		{
			if (BuildPipeline.isBuildingPlayer)
				return;

			AddSceneToList(scene.name, scene.path, recentScenes, MAX_SCENES_RECENTS);
		}
		#endregion
	}
}