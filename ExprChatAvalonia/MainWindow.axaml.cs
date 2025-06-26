using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ExprChatAvalonia
{
	public partial class MainWindow : Window
	{
		IStorageFolder imagesFolder;
		List<string> modelNames = new List<string>();

		public MainWindow()
		{
			InitializeComponent();
		}

		private void TextBlock_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
			BeginMoveDrag(e);
		}

		private void InfoBox(string msg = "", string title = "") {
			if (InfoBoxTitle is null || InfoBoxBody is null) return;
			InfoBoxTitle.Text = title;
			InfoBoxBody.Text = msg;
			InfoBoxTitle.IsVisible = InfoBoxTitle.Text.Length > 0;
			FlyoutBase.ShowAttachedFlyout(this);
		}

        private void ButtonCloseFlyout_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
            FlyoutBase.GetAttachedFlyout(this)?.Hide();
        }

        private async void UpdateDialog (bool slow = false) {
			string latestMsg = Regex.Match(Chat.context, $@"[\s\S\n]*{Chat.story.charName}: ([^\n]+)").Groups[1].Value;
			string dialog = latestMsg.Split('|')[0].Trim();
			string action = "";
			if (latestMsg.Contains('|')) action = latestMsg.Split('|')[1].Trim();

            BorderDialog.IsVisible = dialog.Length > 0;
            if (slow) {
				TextBlockDialog.Text = ""; TextBlockAction.Text = "";
				foreach (char c in dialog) {
					if (TabControlPages.SelectedIndex > 0) return;
					TextBlockDialog.Text += c;
					await Task.Delay(20);
				}
				foreach (char c in action) {
					if (TabControlPages.SelectedIndex > 0) return;
					TextBlockAction.Text += c;
					await Task.Delay(20);
				}
			} else {
				TextBlockDialog.Text = dialog; TextBlockAction.Text = action;
			}
		}

		private void UpdateContext() {
			TextBoxContext.Text = Chat.context;
		}

		private void toggleButtonControls(bool enable) {
			ButtonSend.IsEnabled = enable;
			ButtonRetry.IsEnabled = enable;
			ButtonGen.IsEnabled = enable;
			ButtonRegen.IsEnabled = enable;
		}

		private void toggleInputControls(bool enable) {
			TextBoxInput.IsEnabled = enable;
			TextBoxInputAct.IsEnabled = enable;
			if (enable) {
				TextBoxInput.Text = "";
				TextBoxInputAct.Text = "";
				TextBoxInput.Focus();
			}
		}

        private async Task UpdateStatus() {
			ProgressBarStat.IsIndeterminate = true;
			SolidColorBrush def = ProgressBarStat.Foreground as SolidColorBrush;

			for (int i = 1; i <= 10; i++) {
				ProgressBarStat.Opacity = i / 10.0;
				await Task.Delay(100);
			}

            while (Chat.rs == Chat.reqStat.Waiting) await Task.Delay(1000);
            while (Chat.rs == Chat.reqStat.Processing) await Task.Delay(1000);

            ProgressBarStat.Foreground = new SolidColorBrush(Colors.Gray);
			ProgressBarStat.IsIndeterminate = false;
			ProgressBarStat.ShowProgressText = true;
			ProgressBarStat.ProgressTextFormat = "Queue position: {0}";
			ProgressBarStat.Maximum = 1;
			while(Chat.rs == Chat.reqStat.Queued) {
				if (ProgressBarStat.Maximum < Chat.rt) ProgressBarStat.Maximum = Chat.rt;
				ProgressBarStat.Value = Chat.rt;
				await Task.Delay(1000);
			}

            ProgressBarStat.Foreground = def;
            ProgressBarStat.Maximum = 1;
			ProgressBarStat.ProgressTextFormat = "Processing: ~{0}s left";
            while (Chat.rs == Chat.reqStat.Processing) {
                if (ProgressBarStat.Maximum < Chat.rt) ProgressBarStat.Maximum = Chat.rt;
                ProgressBarStat.Value = Chat.rt;
                await Task.Delay(1000);
            }

			ProgressBarStat.Value = ProgressBarStat.Maximum;
			ProgressBarStat.ShowProgressText = false;

            for (int i = 9; i >= 0; i--) {
                ProgressBarStat.Opacity = i / 10.0;
                await Task.Delay(20);
            }
        }

        private async void ButtonSend_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
			if ((TextBoxInput.Text ?? "").Length + (TextBoxInputAct.Text ?? "").Length == 0) return;
			if (Chat.useHorde && Chat.model.Length == 0) { InfoBox("Please select a model for AI Horde inference.", "No model"); return; }

			toggleButtonControls(false);
			toggleInputControls(false);
			UpdateStatus();
			
			Chat.AppendUserMsg(TextBoxInput.Text ?? "", TextBoxInputAct.Text ?? "");
			try { await Chat.GenerateResponse(); }
			catch (Exception ex) { InfoBox(ex.Message, "An exception occured"); Chat.rs = Chat.reqStat.Idle; }
			UpdateDialog(true);
			UpdateContext();

			ImageChar.Opacity = 0.9;
			string expr = "";
			try { expr = await Chat.GenerateExpression(); }
			catch (Exception ex) { InfoBox(ex.Message, "An exception occured"); }
			if (Chat.icons.ContainsKey(expr)) {
				ImageChar.Source = Chat.icons[expr];
			}
			ImageChar.Opacity = 1.0;

			toggleButtonControls(true);
			toggleInputControls(true);
		}

		private async void ButtonRetry_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
			toggleButtonControls(false);
            UpdateStatus();

            try { await Chat.RetryResponse(); }
			catch (Exception ex) { InfoBox(ex.Message, "An exception occured"); Chat.rs = Chat.reqStat.Idle; }
			UpdateDialog(true);
			UpdateContext();

			ImageChar.Opacity = 0.9;
			string expr = "";
			try { expr = await Chat.GenerateExpression(); }
			catch (Exception ex) { InfoBox(ex.Message, "An exception occured"); }
			if (Chat.icons.ContainsKey(expr)) {
				ImageChar.Source = Chat.icons[expr];
			}
			ImageChar.Opacity = 1.0;

			toggleButtonControls(true);
		}

		private async void ButtonGen_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
			toggleButtonControls(false);
            UpdateStatus();
            try { await Chat.GenerateResponse(); }
			catch (Exception ex) { InfoBox(ex.Message, "An exception occured"); Chat.rs = Chat.reqStat.Idle; }
			UpdateContext();
			toggleButtonControls(true);
		}

		private async void ButtonRegen_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
			toggleButtonControls(false);
            UpdateStatus();
            try { await Chat.RetryResponse(); }
			catch (Exception ex) { InfoBox(ex.Message, "An exception occured"); Chat.rs = Chat.reqStat.Idle; }
			UpdateContext();
			toggleButtonControls(true);
		}

		private void TextBoxInput_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e) {
			if (e.Key == Avalonia.Input.Key.Enter) ButtonSend_Click(sender, e);
		}

		private async void ImageChar_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) {
			if (!ButtonSend.IsEnabled) return;
			ImageChar.Opacity = 0.9;
			string expr = "";
			try { expr = await Chat.GenerateExpression(); }
			catch (Exception ex) { InfoBox(ex.Message, "An exception occured"); }
			if (Chat.icons.ContainsKey(expr)) {
				ImageChar.Source = Chat.icons[expr];
			}
			ImageChar.Opacity = 1.0;
		}

		private void TextBoxContext_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) {
			Chat.context = TextBoxContext.Text;
		}

		private async void TabControl_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e) {
			if (e.Source is null) return;
			if (e.Source is not TabControl) return;
			if ((e.Source as Control)?.Name != "TabControlPages" && (e.Source as Control)?.Name != "TabControlProvider") return;
			switch(TabControlPages?.SelectedIndex) {
				case 0: UpdateDialog(); break;
				case 1: UpdateContext(); TextBoxContext.CaretIndex = TextBoxContext.Text.Length; break;
				case 2: {
					TextBoxUserName.Text = Chat.story.userName;
					TextBoxCharName.Text = Chat.story.charName;
					TextBoxUserDesc.Text = Chat.story.userDesc;
					TextBoxCharDesc.Text = Chat.story.charDesc;
					TextBoxScenario.Text = Chat.story.scenario;
					TextBoxExamples.Text = Chat.story.examples;
					break;
				}
				case 4: {
                    Chat.useHorde = TabControlProvider.SelectedIndex == 1;
                    if (TabControlProvider.SelectedIndex == 0) {
						TextBlockLocal.Text = "⏳ Checking...";
						string model = await Chat.GetLocalModel();
						if (model != null) {
							TextBlockLocal.Text = $"✔ Connected!\nLoaded model: {model.Split("/")[^1]}";
						} else {
							TextBlockLocal.Text = "❌ Couldn't reach local KoboldCpp server.";
						}
                    } else {
						if (TextBoxApi?.Text?.Length == 0) {
							TextBlockUser.Text = "☑ Anonymous";
							TextBlockKudos.Text = "";
							Chat.apiKey = "0000000000";
						} else {
							TextBlockUser.Text = "⏳ Checking...";
							var info = await Chat.GetHordeUser(TextBoxApi.Text);
							if (info.name != null) {
								TextBlockUser.Text = $"☑ {info.name.Replace("(", "\n(")}";
								TextBlockKudos.Text = $"{info.kudos} kudos";
								Chat.apiKey = TextBoxApi.Text;
							} else {
								TextBlockUser.Text = "❌ User not found.";
								TextBlockKudos.Text = "";
								Chat.apiKey = "0000000000";
							}
						}

						modelNames = await Chat.GetHordeModels();
						ComboBoxModel.Items.Clear();
						foreach (string m in modelNames) {
							ComboBoxItem item = new ComboBoxItem();
							item.Content = m.Split("/")[^1];
							if (m.Contains("debug")) item.Opacity = 0.6;
							ComboBoxModel.Items.Add(item);
						}

						foreach (ComboBoxItem item in ComboBoxModel.Items) {
							if (Chat.model.EndsWith(item.Content.ToString()))
								ComboBoxModel.SelectedItem = item;
						}
					}
                    break;
				}
			}
		}

		private void TextBoxUserName_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) { Chat.story.userName = TextBoxUserName.Text.Length > 0 ? TextBoxUserName.Text : "User"; Chat.AssembleMemory(); }
		private void TextBoxCharName_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) { Chat.story.charName = TextBoxCharName.Text.Length > 0 ? TextBoxCharName.Text : "AI"; Chat.AssembleMemory(); }
		private void TextBoxUserDesc_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) { Chat.story.userDesc = TextBoxUserDesc.Text; Chat.AssembleMemory(); }
		private void TextBoxCharDesc_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) { Chat.story.charDesc = TextBoxCharDesc.Text; Chat.AssembleMemory(); }
		private void TextBoxScenario_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) { Chat.story.scenario = TextBoxScenario.Text; Chat.AssembleMemory(); }
		private void TextBoxExamples_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e) { Chat.story.examples = TextBoxExamples.Text; Chat.AssembleMemory(); }

		private void SliderContext_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { Chat.config.contextSize = (int)SliderContext.Value; NumericUpDownContext.Value = Chat.config.contextSize; }
		private void NumericUpDownContext_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e) { Chat.config.contextSize = (int)NumericUpDownContext.Value; SliderContext.Value = Chat.config.contextSize; }
		private void SliderOutput_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { Chat.config.outputLength = (int)SliderOutput.Value; NumericUpDownOutput.Value = Chat.config.outputLength; }
		private void NumericUpDownOutput_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e) { Chat.config.outputLength = (int)NumericUpDownOutput.Value; SliderOutput.Value = Chat.config.outputLength; }
		private void SliderTemp_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { Chat.config.temperature = SliderTemp.Value; NumericUpDownTemp.Value = (decimal)Chat.config.temperature; }
		private void NumericUpDownTemp_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e) { Chat.config.temperature = (double)NumericUpDownTemp.Value; SliderTemp.Value = Chat.config.temperature; }
		private void SliderRepPen_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { Chat.config.repetitionPenalty = SliderRepPen.Value; NumericUpDownRepPen.Value = (decimal)Chat.config.repetitionPenalty; }
		private void NumericUpDownRepPen_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e) { Chat.config.repetitionPenalty = (double)NumericUpDownRepPen.Value; SliderRepPen.Value = Chat.config.repetitionPenalty; }
		private void SliderTopP_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { Chat.config.topP = SliderTopP.Value; NumericUpDownTopP.Value = (decimal)Chat.config.topP; }
		private void NumericUpDownTopP_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e) { Chat.config.topP = (double)NumericUpDownTopP.Value; SliderTopP.Value = Chat.config.topP; }
		private void SliderTopK_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { Chat.config.topK = (int)SliderTopK.Value; NumericUpDownTopK.Value = Chat.config.topK; }
		private void NumericUpDownTopK_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e) { Chat.config.topK = (int)NumericUpDownTopK.Value; SliderTopK.Value = Chat.config.topK; }
		private void SliderNSigma_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) { Chat.config.tnsigma = SliderNSigma.Value; NumericUpDownNSigma.Value = (decimal)Chat.config.tnsigma; }
		private void NumericUpDownNSigma_ValueChanged(object? sender, Avalonia.Controls.NumericUpDownValueChangedEventArgs e) { Chat.config.tnsigma = (double)NumericUpDownNSigma.Value; SliderNSigma.Value = Chat.config.tnsigma; }

        private async void ButtonFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) {
            IReadOnlyList<IStorageFolder> list = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose portraits catalog folder", SuggestedStartLocation = imagesFolder, AllowMultiple = false });
			if (list.Count == 0) return;
			imagesFolder = list[0];
			Chat.imagesDir = Regex.Match(imagesFolder.Path.ToString(), @"\/\/\/(.+)\/").Groups[1].Value.Replace('/', '\\');

			ComboBoxChar.Items.Clear();
			string[] folders = Directory.GetDirectories(Chat.imagesDir);
			foreach (string f in folders) {
				ComboBoxItem item = new ComboBoxItem();
				item.Content = f.Split('\\')[^1];
				ComboBoxChar.Items.Add(item);
			}
			ComboBoxChar.IsEnabled = ComboBoxChar.Items.Count > 0;

			if (!ComboBoxChar.IsEnabled) {
				Chat.selectedFolder = "";
				Chat.loadIcons();
                TextBlockIcons.Text = $"Loaded {Chat.icons.Count} images\n{string.Join(", ", Chat.icons.Keys)}";
				ComboBoxChar.PlaceholderText = Chat.imagesDir.Split("\\")[^1];
            } else {
				ComboBoxChar.PlaceholderText = "Pick...";
			}
        }

        private void ComboBoxChar_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e) {
			Chat.selectedFolder = (ComboBoxChar.SelectedItem as ComboBoxItem)?.Content.ToString();
			Chat.loadIcons();
			TextBlockIcons.Text = $"Found {Chat.icons.Count} portraits\n{string.Join(", ", Chat.icons.Keys)}";
        }

		private async void TextBoxApi_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e) {
			if (e.Key == Avalonia.Input.Key.Enter) {
				if (TextBoxApi?.Text?.Length == 0) {
					TextBlockUser.Text = "☑ Anonymous";
					TextBlockKudos.Text = "";
					Chat.apiKey = "0000000000";
					return;
				}

				TextBlockUser.Text = "⏳ Checking...";
				var info = await Chat.GetHordeUser(TextBoxApi.Text);
				if (info.name != null) {
					TextBlockUser.Text = $"☑ {info.name.Replace("(", "\n(")}";
					TextBlockKudos.Text = $"{info.kudos} kudos";
                    Chat.apiKey = TextBoxApi.Text;
                } else {
					TextBlockUser.Text = "❌ User not found.";
					TextBlockKudos.Text = "";
					Chat.apiKey = "0000000000";
                }
			}
        }

        private void ComboBoxModel_SelectionChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e) {
			if (e.Source is null) return;
			if (modelNames.Count == 0 || ComboBoxModel?.SelectedIndex is null || ComboBoxModel.SelectedIndex < 0 || ComboBoxModel.SelectedIndex >= modelNames.Count) return;
			Chat.model = modelNames[ComboBoxModel.SelectedIndex];
        }
    }
}