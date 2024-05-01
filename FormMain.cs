﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WLTHDBWUI
{
    public partial class FormMain : Form
    {
        /// <summary>
        /// 実行モジュールのあるディレクトリ
        /// </summary>
        private string appDirectory;

        /// <summary>
        /// .logファイル一覧のログファイル名(大文字化後)を覚えておく
        /// </summary>
        private Dictionary<string, string> logFilenames = new Dictionary<string, string>();

        /// <summary>
        /// プログラム設定を保持する
        /// </summary>
        private ConfigBag configBag = new ConfigBag();

        /// <summary>
        /// コンスタラクタ
        /// </summary>
        public FormMain()
        {
            InitializeComponent();

            // コンソール用のテキストボックスを設定する
            Commons.SetConsoleTextBox(this.textBoxConsole);

            this.appDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            this.configBag.Initialize(appDirectory);

            this.CreateDirectory(this.configBag.DbDirectory);
            this.CreateDirectory(this.configBag.LogDirectory);
            this.CreateDirectory(this.configBag.LogBackupDirectory);
            this.CreateDirectory(this.configBag.CsvDirectory);
        }

        /// <summary>
        /// ディレクトリを作成する
        /// </summary>
        /// <param name="path"></param>
        private void CreateDirectory(string path)
        {
            if (!Directory.Exists(path)) {
                try {
                    Directory.CreateDirectory(path);
                } catch (Exception ex) {
                    Commons.WriteLine(ex.Message);
                }
            }
        }

        /// <summary>
        /// コントロールの有効/無効を設定する
        /// </summary>
        private void ControlsEnabler(bool enabled)
        {
            this.tabControlMain.Enabled = enabled;
            this.textBoxConsole.Enabled = enabled;
            this.buttonClearConsole.Enabled = enabled;
        }

        /// <summary>
        /// コントロールを有効化する
        /// </summary>
        private void EnableControls()
        {
            this.ControlsEnabler(true);
        }

        /// <summary>
        /// コントロールを無効化する
        /// </summary>
        private void DisableControls()
        {
            this.ControlsEnabler(false);
        }

        #region イベントハンドラ

        /// <summary>
        /// フォームロード時
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FormMain_Load(object sender, EventArgs e)
        {
            // AssemblyVersion をタイトルに設定する
            {
                AssemblyName assemblyName = Assembly.GetExecutingAssembly().GetName();
                string assemblyVersion = assemblyName.Version.ToString();
                this.Text = string.Format("{0} - {1}", this.Text, assemblyVersion);
            }

            //
            // [CSVの作成]
            //

            // [CSVに使用するWLIDとエイリアス名]の一覧を設定する
            foreach (KeyValuePair<string,string> wlIdAliase in this.configBag.WlIdAliases) {
                string wlId = wlIdAliase.Key;
                string alias = wlIdAliase.Value;
                this.listViewWlIdsAndAliases.Items.Add(new ListViewItem(new string[] { wlId, alias }));
            }

            // [対象期間]を設定する
            {
                // [終了日]には本日の日付を、
                // [開始日]には本日から「プログラム設定の[出力するCSVの日付の日の範囲(差)]」分の過去の日付を設定する
                this.dateTimePickerEndDate.Value = DateTime.Now;
                DateTime dt = this.dateTimePickerEndDate.Value;
                DateTime dtStart = dt.AddDays(-this.configBag.OutputCsvDateDayRange);
                this.dateTimePickerStartDate.Value = dtStart;
            }

            // [CSV作成後関連付け起動する]
            {
                this.checkBoxExecCsv.Checked = this.configBag.ExecCSV;
            }
        }

        /// <summary>
        /// フォームを閉じるとき
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FormMain_FormClosed(object sender, FormClosedEventArgs e)
        {
            // プログラム設定
            {
                // WLIDエイリアス名
                {
                    this.configBag.WlIdAliases.Clear();
                    foreach (ListViewItem item in this.listViewWlIdsAndAliases.Items) {
                        string wlId = item.Text;
                        string alias = item.SubItems[1].Text;
                        this.configBag.WlIdAliases.Add(new KeyValuePair<string,string>(wlId, alias));
                    }
                }

                // [出力するCSVの日付の日の範囲(差)]
                {
                    TimeSpan ts = this.dateTimePickerEndDate.Value - this.dateTimePickerStartDate.Value;
                    this.configBag.OutputCsvDateDayRange = ts.Days;
                }

                // [CSV作成後関連付け起動する]
                {
                    this.configBag.ExecCSV = checkBoxExecCsv.Checked;
                }

                this.configBag.Save();
            }
        }

        #endregion

        #region コンソール

        #region イベントハンドラ

        private void buttonClearConsole_Click(object sender, EventArgs e)
        {
            textBoxConsole.Clear();
        }


        #endregion

        #endregion

        #region .logファイルの取り込み

        /// <summary>
        /// スレッド側からの通知に応じて.logファイル一覧にファイル名を追加する
        /// </summary>
        /// <param name="logFilename"></param>
        private void onListViewLogFilesProgressChanged(string logFilename)
        {
            this.listViewLogFiles.Items.Add(new ListViewItem(logFilename));
        }

        /// <summary>
        /// .logファイル一覧をクリアする
        /// </summary>
        private void ClearLogFiles()
        {
            this.listViewLogFiles.Items.Clear();
            this.logFilenames.Clear();
        }

        #region イベントハンドラ

        /// <summary>
        /// ログファイル一覧 - ドラッグ
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listViewLogFiles_DragEnter(object sender, DragEventArgs e)
        {
            string[] filenpaths = (string[])e.Data.GetData(DataFormats.FileDrop);
            bool bDrag = false;

            if (e.Data.GetDataPresent(DataFormats.FileDrop)) {
                foreach (string filepath in filenpaths) {
                    string ucFilepath = filepath.ToUpper();

                    if (ucFilepath.EndsWith(".LOG")) {
                        bDrag = true;
                        // .log が含まれているときは処理する
                        break;
                    }
                }

                if (bDrag) {
                    e.Effect = DragDropEffects.Copy;
                }
            }
        }

        /// <summary>
        /// ログファイル一覧 - ドロップ
        /// </summary>
        /// <param name="logFilename"></param>
        private async void listViewLogFiles_DragDrop(object sender, DragEventArgs e)
        {
            string[] filepaths = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (filepaths.Length <= 0) {
                return;
            }

            this.DisableControls();

            IProgress<string> progress = new Progress<string>(onListViewLogFilesProgressChanged);
            await Task.Run(() =>
            {
                foreach (string filepath in filepaths) {
                    string ucFilepath = filepath.ToUpper();

                    if (!ucFilepath.EndsWith(".LOG")) {
                        // .log 以外は処理しない
                        continue;
                    }

                    string logFilename = Path.GetFileName(filepath);
                    string ucLogFilename = logFilename.ToUpper();

                    string destLogFilepath = Path.Combine(this.configBag.LogDirectory, logFilename);

                    // 未処理のもののみ処理する
                    if (!this.logFilenames.ContainsKey(ucLogFilename)) {

                        bool success = false;
                        try {
                            // コピーする
                            File.Copy(filepath, destLogFilepath, true);
                            success = true;
                        } catch (Exception ex) {
                            Commons.WriteLine(ex.Message);
                        } finally {
                            if (success) {
                                this.logFilenames.Add(ucLogFilename, logFilename);
                                progress.Report(logFilename);
                            }
                        }
                    }
                }
            });

            this.EnableControls();
        }

        /// <summary>
        /// [一覧をクリア]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonClearLogFiles_Click(object sender, EventArgs e)
        {
            ClearLogFiles();
        }

        /// <summary>
        /// [ログフォルダを開く]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonOpenLogDirectory_Click(object sender, EventArgs e)
        {
            try {
                System.Diagnostics.Process.Start(this.configBag.LogDirectory);
            } catch (Exception ex) {
                Commons.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// [バックアップフォルダを開く]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonOpenBackupDirectory_Click(object sender, EventArgs e)
        {
            try {
                System.Diagnostics.Process.Start(this.configBag.LogBackupDirectory);
            } catch (Exception ex) {
                Commons.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// [ログフォルダの内容を反映する]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void buttonLoadLogFilesFromLogDirectory_Click(object sender, EventArgs e)
        {
            this.DisableControls();
            this.ClearLogFiles();

            IProgress<string> progress = new Progress<string>(onListViewLogFilesProgressChanged);
            await Task.Run(() =>
            {
                try {
                    string[] filenpaths = Directory.GetFiles(this.configBag.LogDirectory, "*.log", SearchOption.TopDirectoryOnly);
                    foreach (string filepath in filenpaths) {
                        string logFilename = Path.GetFileName(filepath);
                        string ucLogFilename = logFilename.ToUpper();
                        if (!this.logFilenames.ContainsKey(ucLogFilename)) {
                            this.logFilenames.Add(ucLogFilename, logFilename);
                            progress.Report(logFilename);
                        }
                    }
                } catch (Exception ex) {
                    Commons.WriteLine(ex.Message);
                }
            });

            this.EnableControls();
        }

        /// <summary>
        /// [.logファイルを内部データベースに取り込む]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void buttonLogFilesToDb_Click(object sender, EventArgs e)
        {
            List<string> logFilepaths = new List<string>();

            foreach (string logFilename in this.logFilenames.Values) {
                string logFilepath = Path.Combine(configBag.LogDirectory, logFilename);
                if (File.Exists(logFilepath)) {
                    logFilepaths.Add(logFilepath);
                }
            }

            this.DisableControls();

            List<string> successedFilepaths = new List<string>();

            await Task.Run(() =>
            {
                try {
                    using (WLTHDB wltdb = new WLTHDB(this.configBag.DbFilepath)) {

                        foreach (string logFilepath in logFilepaths) {
                            try {

                                Commons.WriteLine($"処理中: {logFilepath}");

                                wltdb.LogFile2Db(logFilepath);

                                string backupLogFilepath = Path.Combine(this.configBag.LogBackupDirectory, Path.GetFileName(logFilepath));
                                // NOTE: System.IO.File.Move の第3引数 bool overwrite はどこいった？
                                //File.Move(logFilepath, backupLogFilepath, true);
                                if (File.Exists(backupLogFilepath)) {
                                    File.Delete(backupLogFilepath);
                                }
                                File.Move(logFilepath, backupLogFilepath);
                                successedFilepaths.Add(logFilepath);

                            } catch (Exception ex) {
                                Commons.WriteLine(ex.Message);
                            }
                        }
                    }

                } catch (Exception ex) {
                    Commons.WriteLine(ex.Message);
                }
            });

            this.ClearLogFiles();

            if (successedFilepaths.Count != logFilepaths.Count) {
                // 失敗したファイルのみ .logファイル一覧に追加する 
                foreach (string logFilepath in logFilepaths) {
                    if (successedFilepaths.Contains(logFilepath)) {
                        continue;
                    }

                    string logFilename = Path.GetFileName(logFilepath);
                    string ucLogFilename = logFilename.ToUpper();

                    this.logFilenames.Add(ucLogFilename, logFilename);
                    onListViewLogFilesProgressChanged(logFilename);
                }
            }

            this.EnableControls();
        }
        #endregion

        #endregion

        #region CSVの作成

        /// <summary>
        /// スレッド側からの通知に応じて新たなWLID一覧にWLIDを追加する
        /// </summary>
        /// <param name="wlId">WLID</param>
        private void onlistViewNewWlIdsProgressChanged(string wlId)
        {
            this.listViewNewWlIds.Items.Add(new ListViewItem(wlId));
        }

        /// <summary>
        /// 内部データベースからCSVを作成
        /// </summary>
        private async void CreateCsvFromDb(CsvType csvType, string csvFilename)
        {
            string dateFrom = this.dateTimePickerStartDate.Value.ToString("yyyy/MM/dd");
            string dateTo = this.dateTimePickerEndDate.Value.ToString("yyyy/MM/dd");

            List<KeyValuePair<string, string>> wlIdAliases = new List<KeyValuePair<string, string>>();
            foreach (ListViewItem item in this.listViewWlIdsAndAliases.Items) {
                wlIdAliases.Add(new KeyValuePair<string, string>(item.Text, item.SubItems[1].Text));
            }

            string csvFilepath = Path.Combine(this.configBag.CsvDirectory, csvFilename);

            this.DisableControls();

            await Task.Run(() =>
            {
                try {
                    using (WLTHDB wltdb = new WLTHDB(this.configBag.DbFilepath)) {
                        wltdb.DbData2Csv(csvFilepath, dateFrom, dateTo, csvType, wlIdAliases);
                    }
                } catch (Exception ex) {
                    Commons.WriteLine(ex.Message);
                }
            });

            this.EnableControls();

            if (checkBoxExecCsv.Checked) {
                try {
                    System.Diagnostics.Process.Start(csvFilepath);
                } catch (Exception ex) {
                    Commons.WriteLine(ex.Message);
                }
            }
        }



        #region イベントハンドラ

        /// <summary>
        /// [新たなWLIDを内部データベースから読み込む]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void buttonLoadNewWlIdsFromDb_Click(object sender, EventArgs e)
        {
            this.DisableControls();

            this.listViewNewWlIds.Items.Clear();

            List<string> wlIdList = new List<string>();
            foreach (ListViewItem item in this.listViewWlIdsAndAliases.Items) {
                wlIdList.Add(item.Text);
            }

            IProgress<string> progress = new Progress<string>(onlistViewNewWlIdsProgressChanged);
            await Task.Run(() =>
            {
                try {
                    using (WLTHDB wltdb = new WLTHDB(this.configBag.DbFilepath)) {
                        List<string> wlIds = wltdb.GetWlIds();
                        foreach (string wlId in wlIds) {
                            if (wlIdList.Contains(wlId)) {
                                continue;
                            }

                            progress.Report(wlId);
                        }
                    }

                } catch (Exception ex) {
                    Commons.WriteLine(ex.Message);
                }
            });

            this.EnableControls();
        }

        /// <summary>
        /// 新たなWLID一覧の選択状態が変更されたとき
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listViewNewWlIds_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.buttonUseForCsv.Enabled = 0 < this.listViewNewWlIds.SelectedItems.Count;
        }

        /// <summary>
        /// [CSVに使用する]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonUseForCsv_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in this.listViewNewWlIds.SelectedItems) {
                string wlId = item.Text;
                // エイリアス名にはWLIDを設定しておく
                this.listViewWlIdsAndAliases.Items.Add(new ListViewItem(new string[] { wlId, wlId }));
                item.Remove();
            }
        }

        /// <summary>
        /// [CSVに使用するWLIDとエイリアス名]の選択状態が変更されたとき
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void listViewWlIdsAndAliases_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool enabled = 0 < this.listViewWlIdsAndAliases.SelectedItems.Count;
            this.textAlias.Enabled = enabled;
            this.buttonUpdateAlias.Enabled = enabled;
            this.buttonRemoveFromWlIdsAndAliases.Enabled = enabled;
            this.buttonMoveUpInWlIdsAndAliases.Enabled = enabled;
            this.buttonMoveDownInWlIdsAndAliases.Enabled = enabled;

            this.labelSelectedWlId.Text = (enabled) ? this.listViewWlIdsAndAliases.SelectedItems[0].Text : "";
            this.textAlias.Text = (enabled) ? this.listViewWlIdsAndAliases.SelectedItems[0].SubItems[1].Text : "";
        }

        /// <summary>
        /// [エイリアス名の更新]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonUpdateAlias_Click(object sender, EventArgs e)
        {
            if (this.listViewWlIdsAndAliases.SelectedItems.Count <= 0) {
                return;
            }

            // エイリアス名が空で更新されないようにする
            string alias = this.textAlias.Text.Trim();
            if (string.IsNullOrEmpty(alias)) {
                return;
            }

            ListViewItem item = listViewWlIdsAndAliases.SelectedItems[0];
            item.SubItems[1].Text = alias;
        }

        /// <summary>
        /// [選択したWLIDを削除する]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonRemoveFromWlIdsAndAliases_Click(object sender, EventArgs e)
        {
            if (this.listViewWlIdsAndAliases.SelectedItems.Count <= 0) {
                return;
            }

            ListViewItem item = listViewWlIdsAndAliases.SelectedItems[0];
            this.listViewWlIdsAndAliases.Items.Remove(item);
        }

        /// <summary>
        /// [上へ]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonMoveUpInWlIdsAndAliases_Click(object sender, EventArgs e)
        {
            if (this.listViewWlIdsAndAliases.SelectedItems.Count <= 0) {
                return;
            }

            ListViewItem item = this.listViewWlIdsAndAliases.SelectedItems[0];

            int selectedIndex = this.listViewWlIdsAndAliases.Items.IndexOf(item);
            if (selectedIndex <= 0) {
                return;
            }

            this.listViewWlIdsAndAliases.Items.Remove(item);
            this.listViewWlIdsAndAliases.Items.Insert(selectedIndex - 1, item);
        }

        /// <summary>
        /// [下へ]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonMoveDownInWlIdsAndAliases_Click(object sender, EventArgs e)
        {
            if (this.listViewWlIdsAndAliases.SelectedItems.Count <= 0) {
                return;
            }

            ListViewItem item = this.listViewWlIdsAndAliases.SelectedItems[0];

            int selectedIndex = this.listViewWlIdsAndAliases.Items.IndexOf(item);
            if (this.listViewWlIdsAndAliases.Items.Count - 1 <= selectedIndex) {
                return;
            }

            this.listViewWlIdsAndAliases.Items.Remove(item);
            this.listViewWlIdsAndAliases.Items.Insert(selectedIndex + 1, item);
        }

        /// <summary>
        /// [開始日]が変更された
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dateTimePickerStartDate_ValueChanged(object sender, EventArgs e)
        {
            // 開始日と終了日が逆にならないよう調整する
            TimeSpan ts = this.dateTimePickerEndDate.Value - this.dateTimePickerStartDate.Value;
            if (0 <= ts.Days) {
                return;
            }

            this.dateTimePickerEndDate.Value = this.dateTimePickerStartDate.Value;
        }

        /// <summary>
        /// [終了日]が変更された
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dateTimePickerEndDate_ValueChanged(object sender, EventArgs e)
        {
            TimeSpan ts = this.dateTimePickerEndDate.Value - this.dateTimePickerStartDate.Value;
            if (0 <= ts.Days) {
                return;
            }

            this.dateTimePickerStartDate.Value = this.dateTimePickerEndDate.Value;
        }

        /// <summary>
        /// [CSVファイルのフォルダを開く]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonOpenCSVDirectory_Click(object sender, EventArgs e)
        {
            try {
                System.Diagnostics.Process.Start(this.configBag.CsvDirectory);
            } catch (Exception ex) {
                Commons.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// [温度用CSVを作成]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonCreateTemperatureCsvFromDb_Click(object sender, EventArgs e)
        {
            CreateCsvFromDb(CsvType.Temperature, this.configBag.TemperatureCsvFilename);
        }

        /// <summary>
        /// [湿度用CSVを作成]
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void buttonCreateHumidityCsvFromDb_Click(object sender, EventArgs e)
        {
            CreateCsvFromDb(CsvType.Humidity, this.configBag.HumidityCsvFilename);
        }

        #endregion

        #endregion
    }
}
