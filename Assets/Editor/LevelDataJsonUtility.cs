using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

public static class LevelDataJsonUtility
{
    public static string Export(LevelData ld)
    {
        var dto = LevelDataDto.FromLevelData(ld);
        return JsonUtility.ToJson(dto, true);
    }

    public static void Import(LevelData ld, string json)
    {
        var dto = JsonUtility.FromJson<LevelDataDto>(json);
        if (dto != null)
            dto.ToLevelData(ld);
        EditorUtility.SetDirty(ld);
    }
}

[Serializable]
public class LevelDataDto
{
    public string levelName;
    public int difficulty;
    public float levelLength;
    public string playerPrefabGuid;
    public float playerSpawnX;
    public float playerSpawnY;
    public bool playerFaceRight;
    public bool useCustomInitialCameraPosition;
    public float initialCameraX;
    public float initialCameraY;
    public float cameraDeadZone;
    public bool hasCameraSettings;
    public float levelEndPositionX;
    public string backgroundMode;
    public string backgroundSpriteGuid;
    public float backgroundParallax;
    public int backgroundSortingOrder;

    public List<VariableDto> variables = new List<VariableDto>();
    public List<ElementDto> elements = new List<ElementDto>();
    public List<StoryTriggerDto> storyTriggers = new List<StoryTriggerDto>();
    public List<GroupDto> groups = new List<GroupDto>();

    [Serializable]
    public class VariableDto
    {
        public string name;
        public string type;
        public string defaultValue;
        public string description;
    }

    [Serializable]
    public class ElementDto
    {
        public string id;
        public string name;
        public string type;
        public string prefabGuid;
        public float posX, posY;
        public bool faceRight;
        public float delay;
        public string groupId;
        public List<ConditionDto> conditions = new List<ConditionDto>();
    }

    [Serializable]
    public class ConditionDto
    {
        public string variable;
        public string mode;
        public string value;
    }

    [Serializable]
    public class StoryTriggerDto
    {
        public float posX;
        public string storyId;
        public bool triggerOnce;
        public bool triggerFromLeft;
    }

    [Serializable]
    public class GroupDto
    {
        public string id;
        public string name;
        public float triggerX;
        public bool mustClear;
    }

    public static LevelDataDto FromLevelData(LevelData ld)
    {
        var d = new LevelDataDto
        {
            levelName = ld.levelName,
            difficulty = ld.difficulty,
            levelLength = ld.levelLength,
            playerPrefabGuid = ld.playerPrefab != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ld.playerPrefab)) : "",
            playerSpawnX = ld.playerSpawnPosition.x,
            playerSpawnY = ld.playerSpawnPosition.y,
            playerFaceRight = ld.playerFaceRight,
            useCustomInitialCameraPosition = ld.useCustomInitialCameraPosition,
            initialCameraX = ld.initialCameraPosition.x,
            initialCameraY = ld.initialCameraPosition.y,
            cameraDeadZone = ld.cameraDeadZone,
            hasCameraSettings = true,
            levelEndPositionX = ld.levelEndPositionX,
            backgroundMode = ld.backgroundSettings.mode.ToString(),
            backgroundSpriteGuid = ld.backgroundSettings.singleBackground != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ld.backgroundSettings.singleBackground)) : "",
            backgroundParallax = ld.backgroundSettings.singleParallaxFactor,
            backgroundSortingOrder = ld.backgroundSettings.singleSortingOrder
        };

        foreach (var v in ld.variables)
            d.variables.Add(new VariableDto { name = v.variableName, type = v.type.ToString(), defaultValue = v.defaultValue, description = v.description });

        foreach (var el in ld.elements)
        {
            string guid = el.prefab != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(el.prefab)) : "";
            var conds = new List<ConditionDto>();
            if (el.appearConditions != null)
                foreach (var c in el.appearConditions)
                    conds.Add(new ConditionDto { variable = c.variableName, mode = c.mode.ToString(), value = c.compareValue });

            d.elements.Add(new ElementDto
            {
                id = el.elementId, name = el.displayName, type = el.elementType.ToString(),
                prefabGuid = guid, posX = el.position.x, posY = el.position.y,
                faceRight = el.faceRight, delay = el.appearDelay, groupId = el.groupId,
                conditions = conds
            });
        }

        foreach (var st in ld.storyTriggers)
            d.storyTriggers.Add(new StoryTriggerDto { posX = st.positionX, storyId = st.storyId, triggerOnce = st.triggerOnce, triggerFromLeft = st.triggerFromLeft });

        foreach (var g in ld.groups)
            d.groups.Add(new GroupDto { id = g.groupId, name = g.groupName, triggerX = g.triggerPositionX, mustClear = g.mustClearToProceed });

        return d;
    }

    public void ToLevelData(LevelData ld)
    {
        ld.levelName = levelName;
        ld.difficulty = difficulty;
        ld.levelLength = levelLength;
        ld.playerPrefab = !string.IsNullOrEmpty(playerPrefabGuid) ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(playerPrefabGuid)) : null;
        ld.playerSpawnPosition = new Vector2(playerSpawnX, playerSpawnY);
        ld.playerFaceRight = playerFaceRight;
        ld.useCustomInitialCameraPosition = useCustomInitialCameraPosition;
        ld.initialCameraPosition = new Vector2(initialCameraX, initialCameraY);
        ld.cameraDeadZone = hasCameraSettings ? Mathf.Clamp(cameraDeadZone, 0f, 0.45f) : 0.2f;
        ld.levelEndPositionX = levelEndPositionX;

        if (!string.IsNullOrEmpty(backgroundMode))
            ld.backgroundSettings.mode = (BackgroundMode)Enum.Parse(typeof(BackgroundMode), backgroundMode);
        ld.backgroundSettings.singleBackground = !string.IsNullOrEmpty(backgroundSpriteGuid) ? AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(backgroundSpriteGuid)) : null;
        ld.backgroundSettings.singleParallaxFactor = backgroundParallax;
        ld.backgroundSettings.singleSortingOrder = backgroundSortingOrder;

        ld.variables.Clear();
        if (variables != null)
            foreach (var v in variables)
                ld.variables.Add(new LevelVariableDefinition { variableName = v.name, type = (LevelVariableType)Enum.Parse(typeof(LevelVariableType), v.type), defaultValue = v.defaultValue, description = v.description });

        ld.elements.Clear();
        if (elements != null)
            foreach (var e in elements)
            {
                var conds = new List<LevelVariableCondition>();
                if (e.conditions != null)
                    foreach (var c in e.conditions)
                        conds.Add(new LevelVariableCondition { variableName = c.variable, mode = (LevelVariableCondition.CompareMode)Enum.Parse(typeof(LevelVariableCondition.CompareMode), c.mode), compareValue = c.value });

                ld.elements.Add(new LevelElement
                {
                    elementId = e.id, displayName = e.name, elementType = (ElementType)Enum.Parse(typeof(ElementType), e.type),
                    prefab = !string.IsNullOrEmpty(e.prefabGuid) ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(e.prefabGuid)) : null,
                    position = new Vector2(e.posX, e.posY), faceRight = e.faceRight,
                    appearDelay = e.delay, groupId = e.groupId,
                    appearConditions = conds
                });
            }

        ld.storyTriggers.Clear();
        if (storyTriggers != null)
            foreach (var s in storyTriggers)
                ld.storyTriggers.Add(new StoryTriggerPoint { positionX = s.posX, storyId = s.storyId, triggerOnce = s.triggerOnce, triggerFromLeft = s.triggerFromLeft });

        ld.groups.Clear();
        if (groups != null)
            foreach (var g in groups)
                ld.groups.Add(new ElementGroup { groupId = g.id, groupName = g.name, triggerPositionX = g.triggerX, mustClearToProceed = g.mustClear });
    }
}
