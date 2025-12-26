namespace Kaleidoscope.Models.Settings;

/// <summary>
/// Defines the type of UI control to render for a setting.
/// </summary>
public enum SettingType
{
    /// <summary>Boolean toggle checkbox.</summary>
    Checkbox,
    
    /// <summary>Float slider with min/max range.</summary>
    SliderFloat,
    
    /// <summary>Integer slider with min/max range.</summary>
    SliderInt,
    
    /// <summary>Dropdown combo box for enum values.</summary>
    Combo,
    
    /// <summary>Radio button group for enum values.</summary>
    RadioGroup,
    
    /// <summary>RGBA color picker.</summary>
    ColorEdit,
    
    /// <summary>Single-line text input.</summary>
    TextInput,
    
    /// <summary>Multi-line text input.</summary>
    TextMultiline,
    
    /// <summary>Visual separator line.</summary>
    Separator,
    
    /// <summary>Section header text.</summary>
    Header,
    
    /// <summary>Vertical spacing.</summary>
    Spacing
}
