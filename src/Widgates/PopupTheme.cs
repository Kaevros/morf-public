using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace Widgates
{
    internal static class PopupTheme
    {
        internal static void ApplyMenu(ContextMenu menu)
        {
            menu.Foreground = ThemePalette.Text; menu.Background = ThemePalette.Surface;
            var template = new ControlTemplate(typeof(ContextMenu));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, ThemePalette.Surface); border.SetValue(Border.BorderBrushProperty, ThemePalette.Stroke);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); border.SetValue(Border.PaddingProperty, new Thickness(5));
            border.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter))); template.VisualTree = border; menu.Template = template;
            var style = new Style(typeof(MenuItem)); style.Setters.Add(new Setter(Control.ForegroundProperty, ThemePalette.Text));
            var itemTemplate = new ControlTemplate(typeof(MenuItem));
            var itemBorder = new FrameworkElementFactory(typeof(Border)); itemBorder.Name = "Highlight"; itemBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); itemBorder.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));
            var label = new FrameworkElementFactory(typeof(ContentPresenter)); label.SetValue(ContentPresenter.ContentSourceProperty, "Header"); label.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            itemBorder.AppendChild(label); itemTemplate.VisualTree = itemBorder;
            var selected = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, ThemePalette.Hover, "Highlight")); itemTemplate.Triggers.Add(selected);
            style.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate)); menu.Resources[typeof(MenuItem)] = style;
            var separator = new Style(typeof(Separator)); var separatorTemplate = new ControlTemplate(typeof(Separator));
            var line = new FrameworkElementFactory(typeof(Border)); line.SetValue(Border.BackgroundProperty, ThemePalette.Stroke); line.SetValue(FrameworkElement.HeightProperty, 1.0); line.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 4, 8, 4));
            separatorTemplate.VisualTree = line; separator.Setters.Add(new Setter(Control.TemplateProperty, separatorTemplate)); menu.Resources[typeof(Separator)] = separator;
        }

        internal static void DelayTooltip(DependencyObject target)
        {
            ToolTipService.SetInitialShowDelay(target, 1500);
            ToolTipService.SetBetweenShowDelay(target, 0);
            ToolTipService.SetShowDuration(target, 5000);
        }

        internal static void ApplyControls(ResourceDictionary target)
        {
            var resources = (ResourceDictionary)XamlReader.Parse(Templates);
            resources["MenuSurface"] = ThemePalette.Surface;
            resources["MenuText"] = ThemePalette.Text;
            resources["MenuMuted"] = ThemePalette.Muted;
            resources["MenuStroke"] = ThemePalette.Stroke;
            resources["MenuHover"] = ThemePalette.Hover;
            resources["MenuAccent"] = ThemePalette.Accent;
            target.MergedDictionaries.Add(resources);
        }
        internal static void ApplyScrollBars(ResourceDictionary target)
        {
            // Scope the themed vertical bar to the viewport without restyling its buttons.
            var resources = new ResourceDictionary();
            ApplyControls(resources);
            target["MenuMuted"] = ThemePalette.Muted;
            target["EmptyRepeat"] = resources["EmptyRepeat"];
            target[typeof(System.Windows.Controls.Primitives.ScrollBar)] = resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];
        }

        internal static void ApplySlider(Slider slider)
        {
            var resources = (ResourceDictionary)XamlReader.Parse(Templates);
            resources["MenuSurface"] = ThemePalette.Surface;
            resources["MenuText"] = ThemePalette.Text;
            resources["MenuMuted"] = ThemePalette.Muted;
            resources["MenuStroke"] = ThemePalette.Stroke;
            resources["MenuHover"] = ThemePalette.Hover;
            resources["MenuAccent"] = ThemePalette.Accent;
            slider.Resources.MergedDictionaries.Add(resources);
            slider.Style = (Style)resources[typeof(Slider)];
            DelayTooltip(slider);
        }

        internal static void ApplyTooltips(ResourceDictionary resources)
        {
            var style = new Style(typeof(ToolTip));
            style.Setters.Add(new Setter(Control.ForegroundProperty, ThemePalette.Text));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
            var template = new ControlTemplate(typeof(ToolTip));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, ThemePalette.Surface);
            border.SetValue(Border.BorderBrushProperty, ThemePalette.Stroke);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 6, 10, 6));
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            template.VisualTree = border;
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            resources[typeof(ToolTip)] = style;
        }

        private const string Templates = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='{x:Type Expander}'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Expander}'>
      <StackPanel>
        <ToggleButton x:Name='Header' Content='{TemplateBinding Header}' ContentTemplate='{TemplateBinding HeaderTemplate}'
                      Foreground='{DynamicResource MenuText}' HorizontalContentAlignment='Stretch'
                      IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'
                      AutomationProperties.Name='{Binding Header, RelativeSource={RelativeSource TemplatedParent}}'>
          <ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'>
            <Border x:Name='HeaderSurface' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' CornerRadius='9' Padding='10,10' Cursor='Hand'>
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
                <Path x:Name='Chevron' Data='M 2 0 L 6 4 L 2 8' Stroke='{DynamicResource MenuText}' StrokeThickness='1.5'
                      StrokeStartLineCap='Round' StrokeEndLineCap='Round' VerticalAlignment='Center' HorizontalAlignment='Left'/>
                <ContentPresenter Grid.Column='1' VerticalAlignment='Center' RecognizesAccessKey='True'/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property='IsChecked' Value='True'><Setter TargetName='Chevron' Property='Data' Value='M 0 2 L 4 6 L 8 2'/></Trigger>
              <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='HeaderSurface' Property='Background' Value='{DynamicResource MenuHover}'/></Trigger>
              <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='HeaderSurface' Property='BorderBrush' Value='{DynamicResource MenuText}'/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></ToggleButton.Template>
        </ToggleButton>
        <ContentPresenter x:Name='Body' Visibility='Collapsed' ContentSource='Content'/>
      </StackPanel>
      <ControlTemplate.Triggers>
        <Trigger Property='IsExpanded' Value='True'><Setter TargetName='Body' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='{x:Type CheckBox}'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type CheckBox}'>
      <Grid Background='Transparent'>
        <Border x:Name='Rail' Background='{DynamicResource MenuStroke}' CornerRadius='11' Height='22'/>
        <Border x:Name='Knob' Background='{x:Static SystemColors.HighlightTextBrush}' Width='16' Height='16' CornerRadius='8' HorizontalAlignment='Left' Margin='3,0'/>
        <Border x:Name='Focus' BorderBrush='Transparent' BorderThickness='1' CornerRadius='11'/>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='Rail' Property='Background' Value='{DynamicResource MenuAccent}'/><Setter TargetName='Knob' Property='HorizontalAlignment' Value='Right'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Focus' Property='BorderBrush' Value='{DynamicResource MenuText}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style x:Key='EmptyRepeat' TargetType='{x:Type RepeatButton}'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type RepeatButton}'>
      <Border Background='Transparent'/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='{x:Type Slider}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='MinHeight' Value='28'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Slider}'>
      <Grid Background='Transparent'>
        <Border Height='4' CornerRadius='2' Background='{DynamicResource MenuStroke}' Margin='14,0'/>
        <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
               Value='{TemplateBinding Value}' Orientation='Horizontal'>
          <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'>
            <RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'>
              <Grid Background='Transparent'><Border Height='4' CornerRadius='2' Background='{DynamicResource MenuAccent}' Margin='14,0,0,0'/></Grid>
            </ControlTemplate></RepeatButton.Template>
          </RepeatButton></Track.DecreaseRepeatButton>
          <Track.Thumb><Thumb Width='28' Height='28' Focusable='False'>
            <Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
              <Grid Background='Transparent'><Border x:Name='Handle' Width='15' Height='15' CornerRadius='9' HorizontalAlignment='Center' VerticalAlignment='Center' Background='{DynamicResource MenuText}' BorderBrush='{DynamicResource MenuStroke}' BorderThickness='1'/></Grid>
              <ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Handle' Property='Width' Value='18'/><Setter TargetName='Handle' Property='Height' Value='18'/></Trigger>
                <Trigger Property='IsDragging' Value='True'><Setter TargetName='Handle' Property='Width' Value='18'/><Setter TargetName='Handle' Property='Height' Value='18'/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Thumb.Template>
          </Thumb></Track.Thumb>
          <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource EmptyRepeat}' Command='{x:Static Slider.IncreaseLarge}'/></Track.IncreaseRepeatButton>
        </Track>
        <Border x:Name='FocusRing' BorderBrush='Transparent' BorderThickness='1' CornerRadius='6' IsHitTestVisible='False'/>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='FocusRing' Property='BorderBrush' Value='{DynamicResource MenuAccent}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='{x:Type ScrollBar}'>
    <Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='Width' Value='11'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
      <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'
             Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource EmptyRepeat}' Command='{x:Static ScrollBar.PageUpCommand}'/></Track.DecreaseRepeatButton>
        <Track.Thumb><Thumb Focusable='False'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>
          <Border Background='{DynamicResource MenuMuted}' CornerRadius='3' Margin='3,1'/>
        </ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource EmptyRepeat}' Command='{x:Static ScrollBar.PageDownCommand}'/></Track.IncreaseRepeatButton>
      </Track>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";
    }
}
