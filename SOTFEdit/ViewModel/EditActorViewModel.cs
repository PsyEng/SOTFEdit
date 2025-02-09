﻿using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SOTFEdit.Infrastructure;
using SOTFEdit.Model;
using SOTFEdit.Model.Actors;
using SOTFEdit.Model.Events;
using SOTFEdit.Model.SaveData.Actor;

namespace SOTFEdit.ViewModel;

public partial class EditActorViewModel : ObservableObject
{
    private Actor _actor;
    [ObservableProperty] private short? _actorSelection;

    [ObservableProperty] private ActorModificationMode _modificationMode = ActorModificationMode.Modify;

    [ObservableProperty] private bool _onlyInSameAreaAsActor = true;

    [ObservableProperty] private bool _skipKelvin = true;

    [ObservableProperty] private bool _skipVirginia = true;

    public EditActorViewModel(Actor actor, List<ActorType> allActorTypes)
    {
        Actor = actor;
        AllActorTypes = allActorTypes;
        ActorSelection = AllActorSelections.FirstOrDefault()?.Value;
        ModifyOptions.ReplaceType = Actor.ActorType;
        ModifyOptions.ActorEnergy = Actor.Stats?.GetValueOrDefault("Energy", 100f) ?? 100f;
        ModifyOptions.UpdateEnergy = !Actor.Stats?.ContainsKey("Energy") ?? true;
        ModifyOptions.ActorHealth = Actor.Stats?.GetValueOrDefault("Health", 100f) ?? 100f;
        ModifyOptions.UpdateHealth = !Actor.Stats?.ContainsKey("Energy") ?? true;

        AllInfluences = Influence.AllTypes.Select(type =>
                new ComboBoxItemAndValue<string>(TranslationManager.Get("actors.influenceType." + type), type))
            .ToList();

        switch (actor.TypeId)
        {
            case Constants.Actors.KelvinTypeId:
                SkipKelvin = false;
                break;
            case Constants.Actors.VirginiaTypeId:
                SkipVirginia = false;
                break;
        }
    }

    public WpfObservableRangeCollection<Influence> Influences { get; } = new();

    public List<ComboBoxItemAndValue<string>> AllInfluences { get; }

    public ModifyOptions ModifyOptions { get; } = new();

    public IEnumerable<ComboBoxItemAndValue<short>> AllActorSelections { get; } = new List<ComboBoxItemAndValue<short>>
    {
        new(TranslationManager.Get("actors.modificationOptions.allActorSelections.thisActor"), 0),
        new(TranslationManager.Get("actors.modificationOptions.allActorSelections.allActorsOfSameFamily"), 1),
        new(TranslationManager.Get("actors.modificationOptions.allActorSelections.allActorsOfSameType"), 2),
        new(TranslationManager.Get("actors.modificationOptions.allActorSelections.allActors"), 3)
    };

    public Actor Actor
    {
        get => _actor;
        set
        {
            _actor = value;
            Influences.ReplaceRange(_actor.Influences ?? new List<Influence>());
        }
    }

    public List<ActorType> AllActorTypes { get; }

    [RelayCommand]
    private void AddInfluence(string influenceType)
    {
        if (string.IsNullOrEmpty(influenceType))
        {
            return;
        }

        var existingTypeId = Influences.FirstOrDefault(influence => influence.TypeId == influenceType);
        if (existingTypeId != null)
        {
            return;
        }

        Influences.Add(Influence.AsFillerWithDefaults(influenceType));
        ModifyOptions.UpdateInfluences = true;
    }

    [RelayCommand]
    private void Save()
    {
        WeakReferenceMessenger.Default.Send(new RequestUpdateActorsEvent(this));
    }
}

public partial class ModifyOptions : ObservableObject
{
    [ObservableProperty] private float _actorEnergy;
    [ObservableProperty] private float _actorHealth;
    private float? _originalEnergy;
    private float? _originalHealth;
    [ObservableProperty] private bool _removeSpawner;
    [ObservableProperty] private ActorType? _replaceType;

    [ObservableProperty] private string _teleportMode = "";
    [ObservableProperty] private bool _updateEnergy;
    [ObservableProperty] private bool _updateHealth;

    [ObservableProperty] private bool _updateInfluences;


    partial void OnActorEnergyChanging(float value)
    {
        _originalEnergy ??= value;

        if (_originalEnergy != null && Math.Abs(_originalEnergy.Value - value) < 0.01)
        {
            UpdateEnergy = false;
        }
        else if (Math.Abs(ActorEnergy - value) > 0.01)
        {
            UpdateEnergy = true;
        }
    }

    partial void OnActorHealthChanging(float value)
    {
        _originalHealth ??= value;

        if (_originalHealth != null && Math.Abs(_originalHealth.Value - value) < 0.01)
        {
            UpdateHealth = false;
        }
        else if (Math.Abs(ActorHealth - value) > 0.01)
        {
            UpdateHealth = true;
        }
    }
}