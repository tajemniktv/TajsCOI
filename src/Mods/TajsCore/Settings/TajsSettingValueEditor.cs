// Taj's COI Mods | TajsSettingValueEditor.cs
// Copyright (C) 2026 - 2026 Grzegorz Kaczmarski (TajemnikTV)
// All Rights Reserved.

using System;
using System.Globalization;
using Mafi;
using Mafi.Localization;
using Mafi.Unity.UiToolkit.Component;
using Mafi.Unity.UiToolkit.Library;
using TajsCOI.Common.Settings;
using TextField = Mafi.Unity.UiToolkit.Library.TextField;

namespace TajsCOI.Core.Settings
{
    /// <summary>
    /// Reusable UI shell for <see cref="SettingValueEditorModel"/>. Owners
    /// supply a current typed value and an apply delegate; this control keeps
    /// parsing, dirty/error state, and canonical value display consistent.
    /// </summary>
    public sealed class TajsSettingValueEditor : Column
    {
        private static readonly ColorRgba s_appliedColor = new(130, 220, 150);
        private static readonly ColorRgba s_dirtyColor = new(245, 195, 90);
        private static readonly ColorRgba s_errorColor = new(240, 105, 105);
        private static readonly ColorRgba s_unavailableColor = new(170, 180, 195);

        private readonly SettingValueEditorModel m_model;
        private readonly TextField m_field;
        private readonly Label m_state;
        private readonly Func<bool>? m_isAvailable;

        public TajsSettingValueEditor(
            SettingDescriptor descriptor,
            object authoritativeValue,
            Func<object, SettingSetResult> apply,
            CultureInfo? culture = null,
            Func<bool>? isAvailable = null)
            : base(1.pt())
        {
            m_model = new SettingValueEditorModel(descriptor, authoritativeValue, apply, culture);
            m_isAvailable = isAvailable;
            m_field = new TextField()
                .Text(m_model.Text)
                .MaxWidth(280.px())
                .OnValueChanged(OnInputChanged)
                .OnEditEnd(_ => Commit())
                .OnEscape(Revert);
            m_state = new Label();
            Add(m_field, m_state);
            RefreshAvailability();
            UpdateVisualState();
        }

        public SettingValueEditorModel Model => m_model;

        public void Refresh(object authoritativeValue, SettingApplyMode applyMode = SettingApplyMode.Immediate)
        {
            m_model.Refresh(authoritativeValue, applyMode);
            RefreshAvailability();
            m_field.Text(m_model.Text);
            UpdateVisualState();
        }

        public void RefreshAvailability()
        {
            if (m_isAvailable is null)
            {
                return;
            }
            bool available = false;
            try
            {
                available = m_isAvailable();
            }
            catch (Exception exception)
            {
                m_model.SetAvailable(false, "Availability check failed: " + exception.Message);
            }
            if (m_model.IsAvailable || available)
            {
                m_model.SetAvailable(available, available ? null : "This setting is unavailable.");
            }
        }

        private void OnInputChanged(string text)
        {
            m_model.SetInput(text);
            UpdateVisualState();
        }

        private void Commit()
        {
            RefreshAvailability();
            m_model.TryCommit(out _);
            m_field.Text(m_model.Text);
            UpdateVisualState();
        }

        private void Revert()
        {
            m_model.Revert();
            m_field.Text(m_model.Text);
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            bool available = m_model.IsAvailable;
            m_field.Readonly(!available);
            m_field.MarkAsError(m_model.State == SettingValueEditorState.Invalid, m_model.Error.AsLoc());
            m_state.Value(StateText(m_model).AsLoc()).Color(StateColor(m_model.State));
            if (m_model.State == SettingValueEditorState.Invalid)
            {
                m_state.Tooltip(m_model.Error.AsLoc());
            }
            else if (m_model.State == SettingValueEditorState.Unavailable)
            {
                m_state.Tooltip(m_model.UnavailableReason.AsLoc());
            }
            else
            {
                m_state.Tooltip(null);
            }
        }

        private static string StateText(SettingValueEditorModel model) =>
            model.State switch
            {
                SettingValueEditorState.Dirty => "Edited",
                SettingValueEditorState.Invalid => "Invalid: " + model.Error,
                SettingValueEditorState.RequiresSaveReload => "Accepted · reload save",
                SettingValueEditorState.RequiresRestart => "Accepted · restart game",
                SettingValueEditorState.Unavailable => "Unavailable: " + model.UnavailableReason,
                _ => "Applied",
            };

        private static ColorRgba StateColor(SettingValueEditorState state) =>
            state switch
            {
                SettingValueEditorState.Dirty => s_dirtyColor,
                SettingValueEditorState.Invalid => s_errorColor,
                SettingValueEditorState.Unavailable => s_unavailableColor,
                SettingValueEditorState.RequiresSaveReload => s_dirtyColor,
                SettingValueEditorState.RequiresRestart => s_dirtyColor,
                _ => s_appliedColor,
            };
    }
}
