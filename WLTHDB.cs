﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.IO;

namespace WLTHDBWUI
{
    class WLTHDB : IDisposable
    {
        /// <summary>
        /// データベースコネクション
        /// </summary>
        private SQLiteConnection connection = null;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="dbFilepath">データベースファイルパス</param>
        public WLTHDB(string dbFilepath)
        {
            this.DbOpen(dbFilepath);
            this.CreateTableIfNotExists();
        }

        /// <summary>
        /// デストラクタ
        /// </summary>
        ~WLTHDB()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// 破棄処理
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 破棄処理
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing) {
                this.DbClose();
            }
        }

        /// <summary>
        /// WLID の一覧を取得する
        /// </summary>
        /// <returns></returns>
        public List<string> GetWlIds()
        {
            List<string> wlIds = new List<string>();

            using (var command = new SQLiteCommand(this.connection)) {
                StringBuilder commandText = new StringBuilder();

                commandText.Append("SELECT DISTINCT WLID From WLData");

                command.CommandText = commandText.ToString();

                using (SQLiteDataReader dr = command.ExecuteReader()) {

                    int wlIdIndex = dr.GetOrdinal("WLId");

                    while (dr.Read()) {
                        string wlId = dr.GetString(wlIdIndex);
                        if (string.IsNullOrEmpty(wlId)) {
                            continue;
                        }
                        wlIds.Add(wlId);
                    }
                }
            }

            return wlIds;
        }

        /// <summary>
        /// ひとつのログファイルをデータベースに取り込む
        /// </summary>
        /// <param name="logFilepath">ログファイルパス</param>
        public void LogFile2Db(string logFilepath)
        {
            this.WriteDbFromLogfile(logFilepath);
        }

        /// <summary>
        /// データベースから CSV を作成する
        /// </summary>
        /// <param name="csvFilepath">作成する CSV ファイルパス</param>
        /// <param name="dateFrom">対象とするデータの開始日</param>
        /// <param name="dateTo">対象とするデータの終了日</param>
        /// <param name="csvType">対象とするCSVのタイプ</param>
        /// <param name="wlIdAliases">WLID に対するエイリアス名</param>
        public void DbData2Csv(string csvFilepath, string dateFrom, string dateTo, CsvType csvType, List<KeyValuePair<string, string>> wlIdAliases)
        {
            using (var command = new SQLiteCommand(this.connection)) {
                StringBuilder commandText = new StringBuilder();

                commandText.Append(
                    "SELECT" +
                    " *" +
                    " FROM ("
                    );

                commandText.Append(
                    "SELECT" +
                    " M.LogDate"
                    );

                {
                    int wlIdIndex = 0;
                    foreach (var wlIdAlias in wlIdAliases) {
                        switch (csvType) {
                        case CsvType.Temperature:
                            commandText.AppendFormat(", S{0}.Temperature AS ITEM{0}", wlIdIndex);
                            break;
                        case CsvType.Humidity:
                            commandText.AppendFormat(", S{0}.Humidity AS ITEM{0}", wlIdIndex);
                            break;
                        }
                        ++wlIdIndex;
                    }
                }

                commandText.Append(
                    " FROM (" +
                    "SELECT" +
                    " DISTINCT LogDate" +
                    " FROM WLData" +
                    ") AS M"
                    );

                {
                    int wlIdIndex = 0;
                    foreach (var wlIdAlias in wlIdAliases) {
                        commandText.AppendFormat(
                            " LEFT JOIN WLData AS S{0}" +
                            " ON S{0}.WLId = @P{0}" +
                            " AND S{0}.LogDate = M.LogDate",
                            wlIdIndex
                            );
                        ++wlIdIndex;
                    }
                }

                commandText.AppendFormat(" WHERE '{0} 00:00:00' <= M.LogDate AND M.LogDate <= '{1} 23:59:59'", dateFrom, dateTo);

                commandText.Append(
                    ") ORDER BY LogDate"
                    );

                {
                    int wlIdIndex = 0;
                    foreach (var wlIdAlias in wlIdAliases) {

                        var parameter = command.CreateParameter();
                        parameter.ParameterName = string.Format("P{0}", wlIdIndex);
                        parameter.Value = wlIdAlias.Key.ToUpper();  // ログデータ中の WLID はすべて大文字のはずだが、念のため大文字化しておく
                        command.Parameters.Add(parameter);

                        ++wlIdIndex;
                    }
                }

                command.CommandText = commandText.ToString();

                using (SQLiteDataReader dr = command.ExecuteReader())
                using (StreamWriter sw = new StreamWriter(csvFilepath, false, Encoding.GetEncoding("Shift_JIS"))) {

                    // ヘッダ出力
                    sw.Write("\"LogDate\"");
                    foreach (var wlIdAlias in wlIdAliases) {
                        sw.Write(string.Format(",\"{0}\"", wlIdAlias.Value));
                    }
                    sw.WriteLine();

                    // データ出力
                    int logDateIndex = dr.GetOrdinal("LogDate");

                    while (dr.Read()) {
                        string logDate = dr.GetString(logDateIndex);

                        sw.Write("\"{0}\"", logDate);

                        for (int wlIdIndex = 0; wlIdIndex < wlIdAliases.Count; ++wlIdIndex) {
                            int itemIndex = dr.GetOrdinal(string.Format("ITEM{0}", wlIdIndex));
                            if (!dr.IsDBNull(itemIndex)) {
                                decimal temperature = dr.GetDecimal(itemIndex);
                                sw.Write(",{0}", temperature);
                            } else {
                                // 時刻データに欠損があった場合は空の値を出力する
                                sw.Write(",");
                            }
                        }
                        sw.WriteLine();
                    }
                }
            }
        }

        /// <summary>
        /// ログファイルをデータベースに取り込む
        /// </summary>
        /// <param name="logFilepath">ログファイルパス</param>
        private void WriteDbFromLogfile(string logFilepath)
        {
            using (StreamReader sr = new StreamReader(logFilepath)) {

                string wlId = "UNKOWN";
                bool isHumidity = false;
                int maxColumnsLength = 4;

                while (!sr.EndOfStream) {
                    string line = sr.ReadLine();
                    string[] columns = line.Split(',');
                    if (columns.Length == 0) {
                        continue;
                    }

                    // 先頭レコードのみの動作
                    if (columns[0] == "\"H3\"") {
                        string wwlId = TrimDoubleQuote(columns[4]);
                        if (wwlId.Length != 0) {
                            wlId = wwlId;
                        }

                        if (columns[2] == "12") {
                            // 温湿度タイプ
                            // NOTE: 将来的にもこの判断が有効かは不明
                            isHumidity = true;
                            maxColumnsLength = 5;
                        }
                        continue;
                    }

                    if (columns[0] != "\"SD\"") {
                        continue;
                    }
                    if (columns.Length < maxColumnsLength) {
                        continue;
                    }
                    this.UpsertLogData(wlId, isHumidity, columns);
                }
            }
        }

        /// <summary>
        /// ログデータからデータベースを Upsert する
        /// </summary>
        /// <param name="wlId">WLID</param>
        /// <param name="isHumidity">湿度カラムが存在するかどうか</param>
        /// <param name="columns">カラムデータの配列</param>
        private void UpsertLogData(string wlId, bool isHumidity, string[] columns)
        {
            string logDate = TrimDoubleQuote(columns[1]);
            string temperature = columns[2];
            string humidity = null;
            if (isHumidity) {
                humidity = columns[3];
            }

            using (var command = new SQLiteCommand(this.connection)) {
                command.CommandText =
                    "REPLACE INTO WLData (" +
                    "  WLId" +
                    ", LogDate" +
                    ", Temperature" +
                    ", Humidity" +
                    ") VALUES (" +
                    "  @WLId" +
                    ", @LogDate" +
                    ", @Temperature" +
                    ", @Humidity" +
                    ")"
                    ;
                {
                    // ログデータ中の WLID はすべて大文字のはずだが、念のため大文字化しておく
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@WLId";
                    parameter.Value = wlId.ToUpper();
                    command.Parameters.Add(parameter);
                }
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@LogDate";
                    parameter.Value = logDate;
                    command.Parameters.Add(parameter);
                }
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@Temperature";
                    parameter.Value = temperature;
                    command.Parameters.Add(parameter);
                }
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@Humidity";
                    parameter.Value = humidity;
                    command.Parameters.Add(parameter);
                }

                command.ExecuteNonQuery();
                ////Commons.WriteLine("{0},{1},{2}", wlId, logDate, temperature);
            }
        }

        /// <summary>
        /// データベースを開く
        /// </summary>
        /// <param name="dbFilepath">データベースファイルパス</param>
        private void DbOpen(string dbFilepath)
        {
            var connectionStringBuilder = new SQLiteConnectionStringBuilder { DataSource = dbFilepath };
            this.connection = new SQLiteConnection(connectionStringBuilder.ToString());
            this.connection.Open();
        }

        /// <summary>
        /// データベースを閉じる
        /// </summary>
        private void DbClose()
        {
            if (this.connection != null) {
                this.connection.Close();
            }
        }

        /// <summary>
        /// テーブルがなければ作成する
        /// </summary>
        private void CreateTableIfNotExists()
        {
            using (var command = new SQLiteCommand(this.connection)) {
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS WLData (" +
                    "  WLId TEXT NOT NULL" +
                    ",  LogDate TEXT NOT NULL" +
                    ", Temperature REAL NOT NULL" +
                    ", Humidity REAL NULL" +
                    ", PRIMARY KEY (WLId, LogDate)" +
                    ")"
                    ;
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 文字列前後にダブルクォートがあれば除去する
        /// </summary>
        /// <param name="column">カラムデータ</param>
        /// <returns>処理後の文字列</returns>
        private string TrimDoubleQuote(string column)
        {
            if (column.StartsWith("\"") && column.EndsWith("\"")) {
                return column.Substring(1, column.Length - 2);
            }
            return column;
        }
    }
}
