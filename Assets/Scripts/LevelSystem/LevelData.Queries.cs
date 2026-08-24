using System.Collections.Generic;
using System.Linq;

public partial class LevelData
{
    public ElementGroup FindGroup(string groupId)
    {
        return groups.Find(group => group.groupId == groupId);
    }

    public List<LevelElement> GetElementsByGroup(string groupId)
    {
        return elements.Where(element => element.IsInGroup(groupId)).ToList();
    }

    public List<LevelElement> GetUngroupedElements()
    {
        return elements.Where(element => !element.HasAnyGroup()).ToList();
    }

    public LevelVariableDefinition FindVariable(string name)
    {
        return variables.Find(variable => variable.variableName == name);
    }
}
