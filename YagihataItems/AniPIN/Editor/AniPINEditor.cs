﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using YagihataItems.YagiUtils;

namespace YagihataItems.AniPIN
{
    public class AniPINEditor : EditorWindow
    {
        
        public const string workFolderPath = "Assets/YagihataItems/AniPIN/";
        public const string autoGeneratedFolderPath = workFolderPath + "AutoGenerated/";
        public const string paramName = "AniPINParam";
        public const string mainLayerName = "AniPINMain";
        public const string overlayLayerName = "AniPINOverlay";
        public static readonly string[] systemParams =
        {
            "IsLocal",
            "Viseme",
            "GestureLeft",
            "GestureRight",
            "GestureLeftWeight",
            "GestureRightWeight",
            "AngularY",
            "VelocityX",
            "VelocityY",
            "VelocityZ",
            "Upright",
            "Grounded",
            "Seated",
            "AFK",
            "TrackingType",
            "VRMode",
            "MuteSelf",
            "InStation"
        };
        private const string currentVersion = "anipin_v1.1";
        private const string versionUrl = "https://www.negura-karasu.net/anipin_vercheck/";
        private const string manualUrl = "https://www.negura-karasu.net/archives/1432";
        private const string twitterUrl = "https://twitter.com/Yagihata4x";
        private const VRCExpressionParameters.ValueType IntParam = VRCExpressionParameters.ValueType.Int;
        private const VRCExpressionParameters.ValueType BoolParam = VRCExpressionParameters.ValueType.Bool;
        private AniPINSettings aniPINSettings;
        private VRCAvatarDescriptor avatarRoot = null;
        private VRCAvatarDescriptor avatarRootBefore = null;
        private IndexedList indexedList = new IndexedList();
        private GameObject aniPINSettingsRoot = null;
        private AniPINVariables aniPINVariables;
        private Vector2 ScrollPosition = new Vector2();
        [SerializeField] private Texture2D headerTexture = null;
        private bool showingVerticalScroll = false;
        private string newerVersion = "";
        private TextAsset newVersionTxt = null;
        [MenuItem("Editor/AniPIN")]
        private static void Create()
        {
            GetWindow<AniPINEditor>("AniPIN");
        }
        
        private void OnGUI()
        {
            using (var scrollScope = new EditorGUILayout.ScrollViewScope(ScrollPosition))
            {
                using (var verticalScope = new EditorGUILayout.VerticalScope())
                {
                    ScrollPosition = scrollScope.scrollPosition;
                    if (headerTexture == null)
                        headerTexture = AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(workFolderPath + "Textures/MenuHeader.png");
                    if (verticalScope.rect.height != 0)
                        showingVerticalScroll = verticalScope.rect.height > position.size.y;
                    var height = position.size.x / headerTexture.width * headerTexture.height;
                    if (height > headerTexture.height)
                        height = headerTexture.height;
                    GUILayout.Box(headerTexture, GUILayout.Width(position.size.x - (showingVerticalScroll ? 22 : 8)), GUILayout.Height(height));
                    if (newVersionTxt != null)
                    {
                        newerVersion = newVersionTxt.text.Trim();
                    }
                    else
                        newVersionTxt = AssetDatabase.LoadAssetAtPath<TextAsset>(workFolderPath + "newerVersion.txt");
                    var lastVerCheckHour = AssetDatabase.LoadAssetAtPath<TextAsset>(workFolderPath + "lastVerCheckHour.txt");
                    if (lastVerCheckHour != null)
                    {
                        int lastHour = -1;
                        int.TryParse(lastVerCheckHour.text.Trim(), out lastHour);
                        if (lastHour != DateTime.Now.Hour)
                        {
                            CheckNewerVersion();
                            File.WriteAllText(workFolderPath + "lastVerCheckHour.txt", DateTime.Now.Hour.ToString());
                            EditorUtility.SetDirty(lastVerCheckHour);
                        }
                    }
                    else
                    {
                        CheckNewerVersion();
                        File.WriteAllText(workFolderPath + "lastVerCheckHour.txt", DateTime.Now.Hour.ToString());
                    }
                    using(new EditorGUILayout.HorizontalScope(GUI.skin.box))
                    {
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"VERSION-{currentVersion}");
                    }
                    if (newerVersion.StartsWith("anipin") && currentVersion != newerVersion)
                    {
                        EditorGUILayout.HelpBox($"新しいバージョンの「{newerVersion}」がリリースされています！", MessageType.Info);
                    }
                    EditorGUILayoutExtra.Space();
                    var avatarDescriptors = FindObjectsOfType(typeof(VRCAvatarDescriptor));
                    indexedList.list = avatarDescriptors.Select(n => n.name).ToArray();
                    indexedList.index = EditorGUILayoutExtra.IndexedStringList("対象アバター", indexedList);
                    if (avatarDescriptors.Length <= 0)
                    {
                        EditorGUILayout.HelpBox("VRCAvatarDescriptorが設定されているオブジェクトが存在しません。", MessageType.Error);
                    }
                    else
                    {
                        if (indexedList.index >= 0 && indexedList.index < avatarDescriptors.Length)
                            avatarRoot = avatarDescriptors[indexedList.index] as VRCAvatarDescriptor;
                        else
                            avatarRoot = null;
                        if (avatarRoot == null)
                        {
                            avatarRootBefore = null;
                        }
                        else
                        {
                            //AvatarRootが変更されたら設定を復元
                            if (avatarRoot != avatarRootBefore)
                            {
                                aniPINVariables = new AniPINVariables();
                                RestoreSettings();
                                avatarRootBefore = avatarRoot;
                            }

                            EditorGUILayoutExtra.SeparatorWithSpace();
                            aniPINVariables.AvatarRoot = avatarRoot;
                            aniPINVariables.PINCode = EditorGUILayout.TextField("PINコード", aniPINVariables.PINCode);
                            aniPINVariables.SavePIN = EditorGUILayout.Toggle("アンロック状態を保存する", aniPINVariables.SavePIN);
                            EditorGUILayoutExtra.SeparatorWithSpace();
                            aniPINVariables.WriteDefaults = EditorGUILayout.Toggle("Write Defaults", aniPINVariables.WriteDefaults);
                            var fxLayer = avatarRoot.GetFXLayer(autoGeneratedFolderPath + aniPINVariables.FolderID + "/", false);
                            if (fxLayer != null && !fxLayer.ValidateWriteDefaults(aniPINVariables.WriteDefaults))
                            {
                                EditorGUILayout.HelpBox("WriteDefaultsがFXレイヤー内で統一されていません。\n" +
                                    "このままでも動作はしますが、表情切り替えにバグが発生する場合があります。\n" +
                                    "WriteDefaultsのチェックを切り替えてもエラーメッセージが消えない場合は使用している他のアバターギミックなどを確認してみてください。", MessageType.Warning);
                            }
                            aniPINVariables.OptimizeParams = EditorGUILayout.Toggle("パラメータの最適化", aniPINVariables.OptimizeParams);
                            if (!aniPINVariables.OptimizeParams)
                            {
                                EditorGUILayout.HelpBox("パラメータの最適化が無効になっています。\n" +
                                    "空パラメータや重複パラメータを自動で削除したい場合は\n" +
                                    "パラメータの最適化を行ってください。", MessageType.Warning);
                            }
                            aniPINVariables.ObfuscateAnimator = EditorGUILayout.Toggle("アニメーターの難読化", aniPINVariables.ObfuscateAnimator);
                            if (aniPINVariables.ObfuscateAnimator)
                            {
                                EditorGUILayout.HelpBox("ビルド時にアニメーターを難読化します。\n" +
                                    "エディット時に難読化は行われないため、そのまま改変することができます。\n" +
                                    "VRChat内で他ギミックの動作がおかしい場合などは、難読化を解除してください。", MessageType.Warning);
                            }
                            aniPINVariables.GetInactiveObjects = EditorGUILayout.Toggle("非有効オブジェクトの取得", aniPINVariables.GetInactiveObjects);
                            if (aniPINVariables.GetInactiveObjects)
                            {
                                EditorGUILayout.HelpBox("非表示の対象オブジェクトに、アクティブではないオブジェクトを追加します。\n" +
                                    "別のギミック側でMeshRendererを操作する場合などで、別のギミックの動作が不安定になる場合はチェックを外してください。", MessageType.Warning);
                            }
                            EditorGUILayoutExtra.SeparatorWithSpace();
                            if (GUILayout.Button("適用する"))
                            {
                                SaveSettings();
                                MakeMenu();
                                ApplyToAvatar();
                            }
                            if (GUILayout.Button("適用を解除する"))
                            {
                                SaveSettings();
                                RemoveAutoGenerated();
                            }
                        }
                    }
                    EditorGUILayoutExtra.SeparatorWithSpace();
                    using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                    {
                        EditorGUILayout.HelpBox("AniPINをダウンロードしてくださり、誠にありがとうございます！\n" +
                        "使用法がわからない場合は、下記リンクより説明書をご覧になった上で使ってみてください。\n" +
                        "もしバグや機能追加の要望などありましたら、TwitterのDMで教えていただけますと幸いです。", MessageType.None);
                        EditorGUILayoutExtra.LinkLabel("AniPIN – Avatar Lock System 説明書", Color.blue, new Vector2(), 0, manualUrl);
                        EditorGUILayoutExtra.LinkLabel("Twitter : @Yagihata4x", Color.blue, new Vector2(), 0, twitterUrl);
                    }
                }
            }
        }
        private void MakeMenu()
        {
            YagiAPI.CreateFolderRecursively(autoGeneratedFolderPath + aniPINVariables.FolderID);

            var aniPinMenu = CreateInstance(typeof(VRCExpressionsMenu)) as VRCExpressionsMenu;
            var path = autoGeneratedFolderPath + aniPINVariables.FolderID + "/AniPINMenu.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(aniPinMenu, path);


            var aniPinSubMenu = CreateInstance(typeof(VRCExpressionsMenu)) as VRCExpressionsMenu;
            path = autoGeneratedFolderPath + aniPINVariables.FolderID + "/AniPINSubMenu.asset";
            asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(aniPinSubMenu, path);

            if (avatarRoot.expressionsMenu == null)
                avatarRoot.expressionsMenu = aniPinMenu;
            else if(avatarRoot.expressionsMenu.controls.Any(n => n.name == "AniPIN"))
            {
                avatarRoot.expressionsMenu.controls.First(n => n.name == "AniPIN").subMenu = aniPinMenu;
            }
            else
            {
                if(avatarRoot.expressionsMenu.controls.Any(n => n.name == "Next Page"))
                {
                    avatarRoot.expressionsMenu = aniPinMenu;
                }
                else
                {
                    avatarRoot.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control()
                    {
                        name = "AniPIN",
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = aniPinMenu,
                        icon = AssetDatabase.LoadAssetAtPath<Texture2D>(workFolderPath + "Textures/MenuIcon.png")
                    });
                }
            }
            EditorUtility.SetDirty(avatarRoot.expressionsMenu);
            aniPinMenu.controls.Add(new VRCExpressionsMenu.Control()
            {
                name = "Next Page",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = aniPinSubMenu,
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(workFolderPath + "Textures/MenuButtonNext.png")
            });
            foreach(var v in Enumerable.Range(0, 6))
            {
                aniPinMenu.controls.Add(new VRCExpressionsMenu.Control()
                {
                    name = "PIN " + v.ToString(),
                    type = VRCExpressionsMenu.Control.ControlType.Button,
                    parameter = new VRCExpressionsMenu.Control.Parameter() { name = paramName },
                    value = v + 1,
                    icon = AssetDatabase.LoadAssetAtPath<Texture2D>(workFolderPath + "Textures/MenuButton" + v.ToString() + ".png")
                });
            }
            EditorUtility.SetDirty(aniPinMenu);
            foreach (var v in Enumerable.Range(6, 4))
            {
                aniPinSubMenu.controls.Add(new VRCExpressionsMenu.Control()
                {
                    name = "PIN " + v.ToString(),
                    type = VRCExpressionsMenu.Control.ControlType.Button,
                    parameter = new VRCExpressionsMenu.Control.Parameter() { name = paramName },
                    value = v + 1,
                    icon = AssetDatabase.LoadAssetAtPath<Texture2D>(workFolderPath + "Textures/MenuButton" + v.ToString() + ".png")
                });
            }
            aniPinSubMenu.controls.Add(new VRCExpressionsMenu.Control()
            {
                name = "ENTER",
                type = VRCExpressionsMenu.Control.ControlType.Button,
                parameter = new VRCExpressionsMenu.Control.Parameter() { name = paramName },
                value = 11,
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(workFolderPath + "Textures/MenuButtonEnter.png")
            });
            aniPinSubMenu.controls.Add(new VRCExpressionsMenu.Control()
            {
                name = "RESET",
                type = VRCExpressionsMenu.Control.ControlType.Button,
                parameter = new VRCExpressionsMenu.Control.Parameter() { name = paramName },
                value = 12,
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(workFolderPath + "Textures/MenuButtonReset.png")
            });
            EditorUtility.SetDirty(aniPinSubMenu);

        }
        private void ApplyToAvatar()
        {
            var expParam = avatarRoot.GetExpressionParameters(autoGeneratedFolderPath + aniPINVariables.FolderID + "/");
            if (aniPINVariables.OptimizeParams)
                expParam.OptimizeParameter();
            if (expParam.CheckParameterSpaces(paramName, IntParam))
            {
                var param = expParam.FindParameter(paramName, IntParam);
                if (param == null)
                    expParam.AddParameter(paramName, IntParam, false);
                else
                {
                    param.defaultValue = 0f;
                    param.saved = false;
                    param.valueType = IntParam;
                }
            }
            else
            {
                EditorUtility.DisplayDialog("AniPIN", "エラー : Parametersに空きがありません", "OK");
                return;
            }
            if (expParam.CheckParameterSpaces(aniPINVariables.FolderID, BoolParam))
            {
                var param = expParam.FindParameter(aniPINVariables.FolderID, BoolParam);

                if (param == null)
                    expParam.AddParameter(aniPINVariables.FolderID, BoolParam, aniPINVariables.SavePIN);
                else
                {
                    param.defaultValue = 0f;
                    param.saved = aniPINVariables.SavePIN;
                    param.valueType = BoolParam;
                }
            }
            else
            {
                EditorUtility.DisplayDialog("AniPIN", "エラー : Parametersに空きがありません", "OK");
                return;
            }
            EditorUtility.SetDirty(expParam);
            avatarRoot.customizeAnimationLayers = true;
            avatarRoot.customExpressions = true;
            EditorUtility.SetDirty(avatarRoot);
            var hudTransform = avatarRoot.transform.Find("AniPINOverlay");
            GameObject hudObject;
            if (hudTransform == null)
            {
                hudObject = PrefabUtility.InstantiatePrefab((GameObject)AssetDatabase.LoadAssetAtPath(workFolderPath + "AniPINOverlay.prefab", typeof(GameObject))) as GameObject;
                hudObject.transform.SetParent(avatarRoot.transform);
            }
            else
                hudObject = hudTransform.gameObject;
            hudObject.transform.localPosition = new Vector3(0, 1, 0);
            EditorUtility.SetDirty(hudObject);

            var hudON = new AnimationClip();
            var hudOFF = new AnimationClip();
            var objPath = YagiAPI.GetGameObjectPath(hudObject, avatarRoot.gameObject);

            var curveON = new AnimationCurve();
            curveON.AddKey(new Keyframe(0f, 1));
            curveON.AddKey(new Keyframe(1f / hudON.frameRate, 1));
            hudON.SetCurve(objPath, typeof(GameObject), "m_IsActive", curveON);

            var curveOFF = new AnimationCurve();
            curveOFF.AddKey(new Keyframe(0f, 0));
            curveOFF.AddKey(new Keyframe(1f / hudOFF.frameRate, 0));
            hudOFF.SetCurve(objPath, typeof(GameObject), "m_IsActive", curveOFF);

            AssetDatabase.CreateAsset(hudON, autoGeneratedFolderPath + aniPINVariables.FolderID + "/EnableHUD.anim");
            EditorUtility.SetDirty(hudON);
            AssetDatabase.CreateAsset(hudOFF, autoGeneratedFolderPath + aniPINVariables.FolderID + "/DisableHUD.anim");
            EditorUtility.SetDirty(hudOFF);

            var clipON = new AnimationClip();
            var clipOFF = new AnimationClip();
            var meshRenderers =  new List<MeshRenderer>();
            UnityUtils.GetGameObjectsOfType<MeshRenderer>(ref meshRenderers, avatarRoot.gameObject, aniPINVariables.GetInactiveObjects);
            foreach (var v in meshRenderers)
            {
                objPath = YagiAPI.GetGameObjectPath((v as MeshRenderer).gameObject, avatarRoot.gameObject);
                if (!string.IsNullOrEmpty(objPath))
                {
                    var propValue = 1;
                    if ((v as MeshRenderer).gameObject.name == "AniPINOverlay")
                        propValue = 0;
                    curveON = new AnimationCurve();
                    curveON.AddKey(new Keyframe(0f, propValue));
                    curveON.AddKey(new Keyframe(1f / clipON.frameRate, propValue));
                    clipON.SetCurve(objPath, typeof(MeshRenderer), "m_Enabled", curveON);

                    if (propValue == 0)
                        propValue = 1;
                    else
                        propValue = 0;
                    curveOFF = new AnimationCurve();
                    curveOFF.AddKey(new Keyframe(0f, propValue));
                    curveOFF.AddKey(new Keyframe(1f / clipON.frameRate, propValue));
                    clipOFF.SetCurve(objPath, typeof(MeshRenderer), "m_Enabled", curveOFF);
                }
            }
            var skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
            UnityUtils.GetGameObjectsOfType<SkinnedMeshRenderer>(ref skinnedMeshRenderers, avatarRoot.gameObject, aniPINVariables.GetInactiveObjects);
            foreach (var v in skinnedMeshRenderers)
            {
                objPath = YagiAPI.GetGameObjectPath((v as SkinnedMeshRenderer).gameObject, avatarRoot.gameObject);
                if (!string.IsNullOrEmpty(objPath))
                {
                    var propValue = 1;
                    if ((v as SkinnedMeshRenderer).gameObject.name == "AniPINOverlay")
                        propValue = 0;
                    curveON = new AnimationCurve();
                    curveON.AddKey(new Keyframe(0f, propValue));
                    curveON.AddKey(new Keyframe(1f / clipON.frameRate, propValue));
                    clipON.SetCurve(objPath, typeof(SkinnedMeshRenderer), "m_Enabled", curveON);

                    if (propValue == 0)
                        propValue = 1;
                    else
                        propValue = 0;
                    curveOFF = new AnimationCurve();
                    curveOFF.AddKey(new Keyframe(0f, propValue));
                    curveOFF.AddKey(new Keyframe(1f / clipON.frameRate, propValue));
                    clipOFF.SetCurve(objPath, typeof(SkinnedMeshRenderer), "m_Enabled", curveOFF);
                }
            }
            AssetDatabase.CreateAsset(clipON, autoGeneratedFolderPath + aniPINVariables.FolderID + "/ONAnimation.anim");
            EditorUtility.SetDirty(clipON);
            AssetDatabase.CreateAsset(clipOFF, autoGeneratedFolderPath + aniPINVariables.FolderID + "/OFFAnimation.anim");
            EditorUtility.SetDirty(clipOFF);


            var fxLayer = avatarRoot.GetFXLayer(autoGeneratedFolderPath + aniPINVariables.FolderID + "/");

            var containParameter = fxLayer.parameters.Any(n => n.name == paramName);
            if (!containParameter)
                fxLayer.AddParameter(paramName, AnimatorControllerParameterType.Int);
            containParameter = fxLayer.parameters.Any(n => n.name == aniPINVariables.FolderID);
            if (!containParameter)
                fxLayer.AddParameter(aniPINVariables.FolderID, AnimatorControllerParameterType.Bool);
            containParameter = fxLayer.parameters.Any(n => n.name == "IsLocal");
            if (!containParameter)
                fxLayer.AddParameter("IsLocal", AnimatorControllerParameterType.Bool);


            var layer = fxLayer.FindAnimatorControllerLayer(overlayLayerName);
            if (layer == null)
                layer = fxLayer.AddAnimatorControllerLayer(overlayLayerName);

            var stateMachine = layer.stateMachine;
            stateMachine.Clear();
            var disableOverlayState = stateMachine.AddState("DisableOverlay", new Vector2(240, 240));
            disableOverlayState.writeDefaultValues = aniPINVariables.WriteDefaults;
            var enableOverlayState = stateMachine.AddState("EnableOverlay", new Vector2(240, 480));
            enableOverlayState.writeDefaultValues = aniPINVariables.WriteDefaults;
            stateMachine.defaultState = disableOverlayState;
            var transition = stateMachine.AddAnyStateTransition(enableOverlayState);
            transition.canTransitionToSelf = false;
            transition.duration = 0;
            transition.exitTime = 0;
            transition.name = "EnableOverlay";
            transition.CreateSingleCondition(AnimatorConditionMode.If, "IsLocal", 0, false, false);
            transition = stateMachine.AddAnyStateTransition(disableOverlayState);
            transition.canTransitionToSelf = false;
            transition.duration = 0;
            transition.exitTime = 0;
            transition.name = "DisableOverlay";
            transition.CreateSingleCondition(AnimatorConditionMode.IfNot, "IsLocal", 0, false, false);
            disableOverlayState.motion = hudOFF;
            enableOverlayState.motion = hudON;
            EditorUtility.SetDirty(transition);
            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(disableOverlayState);
            EditorUtility.SetDirty(enableOverlayState);

            layer = fxLayer.FindAnimatorControllerLayer(mainLayerName);
            if (layer == null)
                layer = fxLayer.AddAnimatorControllerLayer(mainLayerName);

            stateMachine = layer.stateMachine;
            stateMachine.Clear();
            var pinCode = aniPINVariables.PINCode;
            var pinLength = pinCode.Length;
            var unlockedState = stateMachine.AddState(string.Format("Unlocked"), new Vector2(240 * pinLength, 360));
            unlockedState.writeDefaultValues = aniPINVariables.WriteDefaults;
            unlockedState.motion = clipON;
            var waitEnterState = stateMachine.AddState(string.Format("WaitEnter"), new Vector2(240 * (pinLength - 1), 360));
            waitEnterState.writeDefaultValues = aniPINVariables.WriteDefaults;
            waitEnterState.motion = clipOFF;
            var resetState = stateMachine.AddState(string.Format("AwaitReset"), new Vector2(0, 480));
            resetState.writeDefaultValues = aniPINVariables.WriteDefaults;
            resetState.motion = clipOFF;
            var resetNeutralState = stateMachine.AddState(string.Format("AwaitNeutral"), new Vector2(-240, 480));
            resetNeutralState.writeDefaultValues = aniPINVariables.WriteDefaults;
            resetNeutralState.motion = clipOFF;
            var driver = resetNeutralState.AddParameterDriver(aniPINVariables.FolderID, 0);

            transition = waitEnterState.MakeTransition(unlockedState, "InputEnter");
            transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 11, true, true);
            EditorUtility.SetDirty(transition);

            transition = waitEnterState.MakeTransition(resetNeutralState, "InputReset");
            transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 12, true, true);
            EditorUtility.SetDirty(transition);

            transition = waitEnterState.MakeTransition(unlockedState, "JumpToUnlock");
            transition.CreateSingleCondition(AnimatorConditionMode.If, aniPINVariables.FolderID, 1, true, true);
            EditorUtility.SetDirty(transition);

            transition = stateMachine.AddAnyStateTransition(unlockedState);
            transition.CreateSingleCondition(AnimatorConditionMode.If, aniPINVariables.FolderID, 1, true, false);
            EditorUtility.SetDirty(transition);

            transition = unlockedState.MakeTransition(resetNeutralState, "InputReset");
            transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 12, true, true);
            EditorUtility.SetDirty(transition);

            transition = unlockedState.MakeTransition(resetNeutralState, "JumpToReset");
            transition.CreateSingleCondition(AnimatorConditionMode.IfNot, aniPINVariables.FolderID, 0, false, false);
            EditorUtility.SetDirty(transition);
            driver = unlockedState.AddParameterDriver(aniPINVariables.FolderID, 1);

            AnimatorState beforePinInputWaitState = null;
            AnimatorState beforeWaitNeutralState = null;
            AnimatorState firstState = null;
            foreach (var n in Enumerable.Range(0, pinLength))
            {
                var pinNum = pinCode[n] - '0';
                var pinInputWaitState = stateMachine.AddState(string.Format("PIN{0}:{1}", n, pinCode[n]), new Vector2(240 * n, 240));
                pinInputWaitState.writeDefaultValues = aniPINVariables.WriteDefaults;
                pinInputWaitState.motion = clipOFF;
                transition = pinInputWaitState.MakeTransition(resetNeutralState, "InputReset");
                transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 12, true, true);
                EditorUtility.SetDirty(transition);
                transition = pinInputWaitState.MakeTransition(unlockedState, "JumpToUnlock");
                transition.CreateSingleCondition(AnimatorConditionMode.If, aniPINVariables.FolderID, 1, false, false);
                EditorUtility.SetDirty(transition);
                if (n != pinLength - 1)
                {
                    var waitNeutralState = stateMachine.AddState(string.Format("PIN{0}:WaitNeutral", n), new Vector2(240 * n, 360));
                    waitNeutralState.writeDefaultValues = aniPINVariables.WriteDefaults;
                    waitNeutralState.motion = clipOFF;
                    transition = waitNeutralState.MakeTransition(unlockedState, "JumpToUnlock");
                    transition.CreateSingleCondition(AnimatorConditionMode.If, aniPINVariables.FolderID, 1, false, false);
                    EditorUtility.SetDirty(transition);
                    transition = waitNeutralState.MakeTransition(resetNeutralState, "InputReset");
                    transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 12, true, true);
                    EditorUtility.SetDirty(transition);

                    //PIN入力待機ステートからのトランジション
                    transition = pinInputWaitState.MakeTransition(waitNeutralState, "CorrectPIN");
                    transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, pinNum + 1, true, true);
                    EditorUtility.SetDirty(transition);
                    transition = pinInputWaitState.MakeTransition(resetState, "IncorrectPIN");
                    transition.name = "IncorrectPIN";
                    transition.conditions = new AnimatorCondition[]
                    {
                        new AnimatorCondition(){ mode = AnimatorConditionMode.Greater, parameter = paramName, threshold = 0 },
                        new AnimatorCondition(){ mode = AnimatorConditionMode.NotEqual, parameter = paramName, threshold = pinNum + 1 },
                        new AnimatorCondition(){ mode = AnimatorConditionMode.If, parameter = "IsLocal", threshold = 0 }
                    };
                    EditorUtility.SetDirty(transition);

                    if (n == 0)
                    {
                        transition = stateMachine.AddAnyStateTransition(pinInputWaitState);
                        transition.CreateSingleCondition(AnimatorConditionMode.IfNot, aniPINVariables.FolderID, 1, true, false);
                        EditorUtility.SetDirty(transition);
                        //stateMachine.AddEntryTransition(pinInputWaitState);
                        firstState = pinInputWaitState;
                    }
                    else
                    {
                        //前回のPINニュートラル待機ステートからのトランジション
                        transition = beforeWaitNeutralState.MakeTransition(pinInputWaitState, "ResetToZero");
                        transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 0, true, true);
                        EditorUtility.SetDirty(transition);
                    }

                    beforeWaitNeutralState = waitNeutralState;
                }
                else
                {
                    //PIN入力待機ステートからのトランジション
                    transition = pinInputWaitState.MakeTransition(waitEnterState, "CorrectPIN");
                    transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, pinNum + 1, true, true);
                    EditorUtility.SetDirty(transition);
                    transition = pinInputWaitState.MakeTransition(resetState, "IncorrectPIN");
                    transition.name = "IncorrectPIN";
                    transition.conditions = new AnimatorCondition[]
                    {
                        new AnimatorCondition(){ mode = AnimatorConditionMode.Greater, parameter = paramName, threshold = 0 },
                        new AnimatorCondition(){ mode = AnimatorConditionMode.NotEqual, parameter = paramName, threshold = pinNum + 1 },
                        new AnimatorCondition(){ mode = AnimatorConditionMode.If, parameter = "IsLocal", threshold = 0 }
                    };
                    EditorUtility.SetDirty(transition);
                    //前回のPINニュートラル待機ステートからのトランジション
                    transition = beforeWaitNeutralState.MakeTransition(pinInputWaitState, "ResetToZero");
                    transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 0, true, true);
                    EditorUtility.SetDirty(transition);
                }
                beforePinInputWaitState = pinInputWaitState;
            }
            transition = resetState.MakeTransition(resetNeutralState, "InputReset");
            transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 12, true, true);
            EditorUtility.SetDirty(transition);
            transition = resetNeutralState.MakeTransition(firstState, "ResetToZero");
            transition.CreateSingleCondition(AnimatorConditionMode.Equals, paramName, 0, true, true);
            EditorUtility.SetDirty(transition);
            stateMachine.defaultState = firstState;
            EditorUtility.SetDirty(fxLayer);
        }
        private void RemoveAutoGenerated()
        {
            var fxLayer = avatarRoot.GetFXLayer(autoGeneratedFolderPath + aniPINVariables.FolderID + "/");
            var param = fxLayer.parameters.FirstOrDefault(n => n.name == aniPINVariables.FolderID);
            fxLayer.TryRemoveParameter(aniPINVariables.FolderID);
            fxLayer.TryRemoveParameter(paramName);
            fxLayer.TryRemoveLayer("AniPINOverlay");
            fxLayer.TryRemoveLayer("AniPINMain");
            if(avatarRoot.expressionParameters != null)
            {
                avatarRoot.expressionParameters.TryRemoveParameter(aniPINVariables.FolderID);
                avatarRoot.expressionParameters.TryRemoveParameter(paramName);
                EditorUtility.SetDirty(avatarRoot.expressionParameters);
            }
            var obj = avatarRoot.transform.Find("AniPINOverlay");
            if(obj != null)
                DestroyImmediate(obj.gameObject);
            EditorUtility.SetDirty(avatarRoot);
            EditorUtility.SetDirty(fxLayer);
            YagiAPI.TryDeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID + "/AniPINMenu.asset");
            YagiAPI.TryDeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID + "/AniPINSubMenu.asset");
            YagiAPI.TryDeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID + "/GeneratedExpressionParameters.asset");
            YagiAPI.TryDeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID + "/GeneratedFXLayer.controller");
            YagiAPI.TryDeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID + "/DisableHUD.anim");
            YagiAPI.TryDeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID + "/EnableHUD.anim");
            YagiAPI.TryDeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID + "/OFFAnimation.anim");
            YagiAPI.TryDeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID + "/ONAnimation.anim");
            AssetDatabase.DeleteAsset(autoGeneratedFolderPath + aniPINVariables.FolderID);
            var searchedFromAvatarRoot = FindObjectsOfType(typeof(AniPINSettings)).FirstOrDefault(n => (n as AniPINSettings).AvatarRoot == avatarRoot);
            if (searchedFromAvatarRoot != null)
                DestroyImmediate((searchedFromAvatarRoot as AniPINSettings).gameObject);
        }
        private void RestoreSettings()
        {
            var searchedFromAvatarRoot = FindObjectsOfType(typeof(AniPINSettings)).FirstOrDefault(n => (n as AniPINSettings).AvatarRoot == avatarRoot);
            aniPINSettings = null;
            if (searchedFromAvatarRoot != null)
            {
                aniPINSettings = searchedFromAvatarRoot as AniPINSettings;
            }
            else
            {
                aniPINSettingsRoot = GameObject.Find("AniPINSettings");
                if (aniPINSettingsRoot != null)
                {
                    GameObject aniPINVariablesObject;
                    var v = aniPINSettingsRoot.transform.Find(avatarRoot.name);
                    if (v != null)
                    {
                        aniPINVariablesObject = v.gameObject;
                        aniPINSettings = aniPINVariablesObject.GetComponent<AniPINSettings>();
                    }
                }
            }
            if (aniPINSettings != null)
            {
                aniPINVariables = aniPINSettings.GetVariables();
            }
            else
            {
                aniPINVariables.FolderID = System.Guid.NewGuid().ToString();
            }
        }
        private void CheckNewerVersion()
        {
            using (var wc = new WebClient())
            {
                try
                {
                    string text = wc.DownloadString(versionUrl);
                    newerVersion = text.Trim();
                    File.WriteAllText(workFolderPath + "newerVersion.txt", newerVersion);
                }
                catch (WebException exc)
                {

                }
            }
        }
        private void SaveSettings()
        {

            aniPINSettingsRoot = GameObject.Find("AniPINSettings");
            if (aniPINSettingsRoot == null)
            {
                aniPINSettingsRoot = new GameObject("AniPINSettings");
                Undo.RegisterCreatedObjectUndo(aniPINSettingsRoot, "Create AniPINSettings Root");
                EditorUtility.SetDirty(aniPINSettingsRoot);
            }
            GameObject aniPINVariablesObject;
            var v = aniPINSettingsRoot.transform.Find(avatarRoot.name);
            if (v == null)
            {
                aniPINVariablesObject = new GameObject(avatarRoot.name);
                Undo.RegisterCreatedObjectUndo(aniPINVariablesObject, "Create AniPINVariables");
                aniPINVariablesObject.transform.SetParent(aniPINSettingsRoot.transform);
            }
            else
                aniPINVariablesObject = v.gameObject;
            aniPINSettings = aniPINVariablesObject.GetComponent<AniPINSettings>();
            if (aniPINSettings == null)
                aniPINSettings = Undo.AddComponent(aniPINVariablesObject, typeof(AniPINSettings)) as AniPINSettings;
            Undo.RecordObject(aniPINSettings, "Update AniPINVariables");
            aniPINSettings.SetVariables(aniPINVariables);
            EditorUtility.SetDirty(aniPINVariablesObject);
            EditorUtility.SetDirty(aniPINSettings);
        }
    }
}
