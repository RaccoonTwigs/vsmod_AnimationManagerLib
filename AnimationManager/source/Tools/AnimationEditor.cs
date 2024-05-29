#if DEBUG

using AnimationManagerLib.API;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace AnimationManagerLib;

internal class AnimationEditor
{
    public AnimationEditor(AnimationProvider animationProvider, AnimationManager manager, ICoreClientAPI api)
    {
        _animationProvider = animationProvider;
        _animationIdEditor = new(animationProvider, manager, api);
        _runSequenceEditor = new();
        _animationManager = manager;
        _api = api;
    }

    public bool Draw(string title)
    {
        if (!ImGui.Begin(title)) return false;

        if (!_registered) ImGui.BeginDisabled();
        if (ImGui.Button("Run animation"))
        {
            _animationIdEditor.Run(_runSequenceEditor.AsArray);
        }
        ImGui.SameLine();
        if (ImGui.Button("Stop animation"))
        {
            _animationIdEditor.Stop();
        }
        if (!_registered) ImGui.EndDisabled();
        if (!_registered)
        {
            ImGui.Text("Failed to register animation, check if animation id and type is correct.");
        }

        ImGui.BeginTabBar("tabs");
        if (ImGui.BeginTabItem("Animation"))
        {
            EditAnimationTab();
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Parameters"))
        {
            RunAnimationTab();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();

        ImGui.End();
        return true;
    }

    private readonly ICoreClientAPI _api;
    private readonly AnimationProvider _animationProvider;
    private readonly RunSequenceEditor _runSequenceEditor;
    private readonly AnimationIdEditor _animationIdEditor;
    private readonly AnimationManager _animationManager;
    private bool _registered;
    private int _animationIndex = 0;
    private bool _setCurrentFrame = false;

    private void RunAnimationTab()
    {
        if(ImGui.Button("Export to clipboard"))
        {
            ImGui.SetClipboardText(_runSequenceEditor.ToJson());
        }
        ImGui.SameLine();
        _runSequenceEditor.Edit("");
    }

    private void EditAnimationTab()
    {
        if (ImGui.Button("Register animation"))
        {
            _registered = _animationIdEditor.Register();
        }
        if (_animationIdEditor.Edit(""))
        {
            _registered = false;
        }
        if (!_registered) return;

        ImGui.Separator();

        if (_animationIndex >= _animationIdEditor.Ids.Count) _animationIndex = 0;
        ImGui.ListBox("Animation", ref _animationIndex, _animationIdEditor.Ids.Select(id => id._debugName).ToArray(), _animationIdEditor.Ids.Count);

        AnimationId animationId = _animationIdEditor.Ids[_animationIndex];

        

        IAnimation? animation = _animationProvider.Get(animationId, new(_api.World.Player.Entity));
        if (animation == null) _registered = false;

        ImGui.Checkbox("Set animation to current frame", ref _setCurrentFrame);
        if (_setCurrentFrame && animation != null)
        {
            _animationManager.Run(new(_api.World.Player.Entity), animationId, RunParameters.Set((animation as Animation).CurrentFrame));
        }

        animation?.Editor("animation");
    }
}

internal class RunSequenceEditor
{
    public List<RunParameters> Sequence { get; } = new();
    public RunParameters[] AsArray => Sequence.ToArray();

    public void Edit(string id)
    {
        RunSequenceEditorControlButtons(id, Sequence);

        if (Sequence.Count == 0) return;

        ImGui.ListBox($"Sequence##{id}", ref _sequenceIndex, Sequence.Select(element => element.Action.ToString()).ToArray(), Sequence.Count);

        Sequence[_sequenceIndex] = RunParametersEditor(id, Sequence[_sequenceIndex]);
    }
    public string ToJson(string indent = "")
    {
        StringBuilder result = new();
        result.Append(indent);
        result.Append("[\n");

        bool first = true;
        foreach (RunParameters parameters in Sequence)
        {
            if (!first)
            {
                result.Append(",\n");
            }
            else
            {
                first = false;
            }

            result.Append(RunParametersToJson(parameters, indent + "  "));
        }

        result.Append("\n]");
        return result.ToString();
    }

    private int _sequenceIndex = 0;

    private static string RunParametersToJson(RunParameters parameters, string indent)
    {
        StringBuilder result = new();
        result.Append(indent);
        result.Append("{\n");
        result.Append($"{indent}  \"Action\" : \"{parameters.Action}\"");


        switch (parameters.Action)
        {
            case AnimationPlayerAction.Set:
                result.Append($",\n{indent}  \"Frame\" : {parameters.TargetFrame}\n");
                break;
            case AnimationPlayerAction.EaseIn:
                result.Append($",\n{indent}  \"DurationMs\" : {parameters.Duration.TotalMilliseconds}");
                result.Append($",\n{indent}  \"Frame\" : {parameters.TargetFrame}");
                result.Append($",\n{indent}  \"EasingFunction\" : \"{parameters.Modifier}\"");
                break;
            case AnimationPlayerAction.EaseOut:
                result.Append($",\n{indent}  \"DurationMs\" : {parameters.Duration.TotalMilliseconds}");
                result.Append($",\n{indent}  \"EasingFunction\" : \"{parameters.Modifier}\"");
                break;
            case AnimationPlayerAction.Play:
            case AnimationPlayerAction.Rewind:
                result.Append($",\n{indent}  \"DurationMs\" : {parameters.Duration.TotalMilliseconds}");
                result.Append($",\n{indent}  \"StartFrame\" : {parameters.TargetFrame}");
                result.Append($",\n{indent}  \"TargetFrame\" : {parameters.TargetFrame}");
                result.Append($",\n{indent}  \"EasingFunction\" : \"{parameters.Modifier}\"");
                break;
            case AnimationPlayerAction.Stop:
            case AnimationPlayerAction.Clear:
                break;
        }

        result.Append(indent);
        result.Append("\n}");
        return result.ToString();
    }
    private void RunSequenceEditorControlButtons(string id, List<RunParameters> sequence)
    {
        if (_sequenceIndex >= sequence.Count)
        {
            _sequenceIndex = Math.Max(0, sequence.Count - 1);
        }

        if (ImGui.Button($"Add##{id}"))
        {
            sequence.Add(RunParameters.Stop());
            _sequenceIndex = sequence.Count - 1;
        }
        ImGui.SameLine();

        bool disabled = sequence.Count == 0;
        if (disabled) ImGui.BeginDisabled();
        if (ImGui.Button($"Remove##{id}"))
        {
            sequence.RemoveAt(_sequenceIndex);
            if (_sequenceIndex >= sequence.Count)
            {
                _sequenceIndex = Math.Max(0, sequence.Count - 1);
            }
        }
        ImGui.SameLine();
        if (ImGui.Button($"Move up##{id}") && _sequenceIndex > 0)
        {
            (sequence[_sequenceIndex - 1], sequence[_sequenceIndex]) = (sequence[_sequenceIndex], sequence[_sequenceIndex - 1]);
        }
        ImGui.SameLine();
        if (ImGui.Button($"Move down##{id}") && sequence.Count - _sequenceIndex > 2)
        {
            (sequence[_sequenceIndex + 1], sequence[_sequenceIndex]) = (sequence[_sequenceIndex], sequence[_sequenceIndex + 1]);
        }
        if (disabled) ImGui.EndDisabled();
    }
    private static RunParameters RunParametersEditor(string id, RunParameters parameters)
    {
        AnimationPlayerAction action = parameters.Action;
        VSImGui.EnumEditor<AnimationPlayerAction>.Combo($"Action##{id}", ref action);

        TimeSpan duration = parameters.Duration;
        ProgressModifierType modifier = parameters.Modifier;
        int durationMs = (int)duration.TotalMilliseconds;
        int targetFrame = (int?)parameters.TargetFrame ?? 0;
        int startFrame = (int?)parameters.StartFrame ?? 0;

        switch (action)
        {
            case AnimationPlayerAction.Set:
                ImGui.InputInt($"Frame##{id}", ref targetFrame);
                break;
            case AnimationPlayerAction.EaseIn:
                ImGui.InputInt($"Duration##{id}", ref durationMs);
                ImGui.InputInt($"Frame##{id}", ref targetFrame);
                VSImGui.EnumEditor<ProgressModifierType>.Combo($"Easing function##{id}", ref modifier);
                break;
            case AnimationPlayerAction.EaseOut:
                ImGui.InputInt($"Duration##{id}", ref durationMs);
                VSImGui.EnumEditor<ProgressModifierType>.Combo($"Easing function##{id}", ref modifier);
                break;
            case AnimationPlayerAction.Play:
                ImGui.InputInt($"Duration##{id}", ref durationMs);
                ImGui.InputInt($"Start frame##{id}", ref startFrame);
                ImGui.InputInt($"End frame##{id}", ref targetFrame);
                VSImGui.EnumEditor<ProgressModifierType>.Combo($"Easing function##{id}", ref modifier);
                break;
            case AnimationPlayerAction.Stop:
                break;
            case AnimationPlayerAction.Rewind:
                ImGui.InputInt($"Duration##{id}", ref durationMs);
                ImGui.InputInt($"Target frame##{id}", ref startFrame);
                ImGui.InputInt($"Start frame##{id}", ref targetFrame);
                VSImGui.EnumEditor<ProgressModifierType>.Combo($"Easing function##{id}", ref modifier);
                break;
            case AnimationPlayerAction.Clear:
                break;
        }

        return new(action, TimeSpan.FromMilliseconds(durationMs), modifier, targetFrame, startFrame);
    }
}

internal class AnimationIdEditor
{
    public AnimationIdEditor(AnimationProvider animationProvider, AnimationManager manager, ICoreClientAPI api)
    {
        _animationProvider = animationProvider;
        _manager = manager;
        _api = api;
    }

    public List<AnimationId> Ids { get; } = new();

    public bool Edit(string id)
    {
        if (ImGui.Button($"Paste##{id}"))
        {
            _animationId = ImGui.GetClipboardText();
        }
        ImGui.SameLine();
        
        string animationIdCopy = (string)_animationId.Clone();
        ImGui.InputText($"Animation id", ref _animationId, 200);
        bool modified = animationIdCopy == _animationId;

        AnimationIdSetType typeCopy = _type;
        VSImGui.EnumEditor<AnimationIdSetType>.Combo($"Animation ids type##{id}", ref _type);
        if (typeCopy != _type) modified = true;

        switch (_type)
        {
            case AnimationIdSetType.Vanilla:
                _vanillaTp = _animationId;
                _vanillaFp = _animationId + "-fp";

                ImGui.Text($"Third person: {_vanillaTp}");
                ImGui.Text($"First person: {_vanillaFp}");
                break;
            case AnimationIdSetType.Extended:
                _extendedTpHands = $"{_animationId}-tp-hands";
                _extendedFpHands = $"{_animationId}-fp-hands";
                _extendedTpLegs = $"{_animationId}-tp-legs";
                _extendedFpLegs = $"{_animationId}-fp-legs";

                ImGui.Text($"Third person (hands): {_extendedTpHands}");
                ImGui.Text($"First person (hands): {_extendedFpHands}");
                ImGui.Text($"Third person (legs): {_extendedTpLegs}");
                ImGui.Text($"First person (legs): {_extendedFpLegs}");
                break;
        }

        if (_exception != "")
        {
            ImGui.Separator();
            ImGui.Text("Error on registering:");
            ImGui.Text(_exception);
        }

        return false;
    }
    public bool Register()
    {
        try
        {
            switch (_type)
            {
                case AnimationIdSetType.Single:
                    RegisterSingle();
                    break;
                case AnimationIdSetType.Vanilla:
                    RegisterVanilla();
                    break;
                case AnimationIdSetType.Extended:
                    RegisterExtended();
                    break;
            }
            _exception = "";
        }
        catch (Exception exception)
        {
            _exception = exception.Message;
            Ids.Clear();
            return false;
        }

        return true;
    }
    public void Run(RunParameters[] parameters)
    {
        Stop();
        foreach (AnimationId id in Ids)
        {
            Guid runId = _manager.Run(new(_api.World.Player.Entity), id, parameters);
            _runs.Add(runId);
        }
    }
    public void Stop()
    {
        foreach (Guid runId in _runs)
        {
            _manager.Stop(runId);
        }
        _runs.Clear();
    }

    private enum AnimationIdSetType
    {
        Single,
        Vanilla,
        Extended
    }

    private readonly List<Guid> _runs = new();
    private readonly ICoreClientAPI _api;
    private readonly AnimationProvider _animationProvider;
    private readonly AnimationManager _manager;
    private AnimationIdSetType _type = AnimationIdSetType.Single;
    private string _exception = "";
    private string _animationId = "";
    private string _vanillaTp = "";
    private string _vanillaFp = "";
    private string _extendedTpHands = "";
    private string _extendedFpHands = "";
    private string _extendedTpLegs = "";
    private string _extendedFpLegs = "";

    private void RegisterSingle()
    {
        AnimationId id = RegisterAnimation(_animationId, "main");

        Ids.Clear();
        Ids.Add(id);
    }
    private void RegisterVanilla()
    {
        AnimationId idTp = RegisterAnimation(_vanillaTp, "main");
        AnimationId idFp = RegisterAnimation(_vanillaFp, "main");

        Ids.Clear();
        Ids.Add(idTp);
        Ids.Add(idFp);
    }
    private void RegisterExtended()
    {
        AnimationId idTpHands = RegisterAnimation(_extendedTpHands, "hands", 512);
        AnimationId idFpHands = RegisterAnimation(_extendedFpHands, "hands", 512);
        AnimationId idTpLegs = RegisterAnimation(_extendedTpLegs, "legs", 16);
        AnimationId idFpLegs = RegisterAnimation(_extendedFpLegs, "legs", 16);

        Ids.Clear();
        Ids.Add(idTpHands);
        Ids.Add(idFpHands);
        Ids.Add(idTpLegs);
        Ids.Add(idFpLegs);
    }
    private AnimationId RegisterAnimation(string code, string category, float weight = 512)
    {
        AnimationId id = new(category, code, EnumAnimationBlendMode.Average, weight);
        AnimationData data = AnimationData.Player(code);
        _animationProvider.Register(id, data);

        return id;
    }
}

#endif