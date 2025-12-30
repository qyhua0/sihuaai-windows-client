using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows;
using Microsoft.Win32; // 用于 OpenFileDialog 和 SaveFileDialog
using MiniExcelLibs;   // 必须引用 MiniExcel
using Bloghua.AutoClient.Core.Entities;
using Bloghua.AutoClient.Infrastructure.Data;
using System.Linq;
using System.Collections.Generic;

namespace Bloghua.AutoClient.Desktop.ViewModels
{
    public class QAViewModel : INotifyPropertyChanged
    {
        private DatabaseService _db = ServiceLocator.Db;

        public ObservableCollection<QuestionAnswer> QAList { get; set; }

        // --- 选中项与编辑字段 ---
        private QuestionAnswer _selectedQA;
        public QuestionAnswer SelectedQA
        {
            get => _selectedQA;
            set
            {
                _selectedQA = value;
                if (value != null)
                {
                    EditingQuestion = value.Question;
                    EditingAnswer = value.Answer;
                    EditingPlatform = value.Platform;
                    EditingPriority = value.Priority;
                }
                OnPropertyChanged();
            }
        }

        private string _editingQuestion;
        public string EditingQuestion { get => _editingQuestion; set { _editingQuestion = value; OnPropertyChanged(); } }

        private string _editingAnswer;
        public string EditingAnswer { get => _editingAnswer; set { _editingAnswer = value; OnPropertyChanged(); } }

        private string _editingPlatform = "WeChat";
        public string EditingPlatform { get => _editingPlatform; set { _editingPlatform = value; OnPropertyChanged(); } }

        private int _editingPriority;
        public int EditingPriority { get => _editingPriority; set { _editingPriority = value; OnPropertyChanged(); } }

        private string _searchKeyword;
        public string SearchKeyword { get => _searchKeyword; set { _searchKeyword = value; OnPropertyChanged(); } }

        private string _statusMessage;
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        // --- 命令 ---
        public ICommand SearchCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ClearSelectionCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ExportCommand { get; }

        public QAViewModel()
        {
            QAList = new ObservableCollection<QuestionAnswer>();
            RefreshList();

            SearchCommand = new RelayCommand(_ => RefreshList());
            SaveCommand = new RelayCommand(_ => Save());
            DeleteCommand = new RelayCommand(_ => Delete());
            ClearSelectionCommand = new RelayCommand(_ => Clear());

            // 【实现导入导出命令】
            ImportCommand = new RelayCommand(_ => ImportExcel());
            ExportCommand = new RelayCommand(_ => ExportExcel());
        }

        private void RefreshList()
        {
            var list = _db.SearchQAs(SearchKeyword, "WeChat"); // 默认显示微信，可通过搜索扩展
            QAList.Clear();
            foreach (var item in list) QAList.Add(item);
            StatusMessage = $"查询到 {list.Count} 条记录";
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(EditingQuestion) || string.IsNullOrEmpty(EditingAnswer))
            {
                MessageBox.Show("问题和回答不能为空");
                return;
            }

            var qa = SelectedQA ?? new QuestionAnswer();
            qa.Question = EditingQuestion;
            qa.Answer = EditingAnswer;
            qa.Platform = EditingPlatform;
            qa.Priority = EditingPriority;

            _db.SaveQA(qa);
            RefreshList();
            Clear();
            StatusMessage = "保存成功";
        }

        private void Delete()
        {
            if (SelectedQA != null)
            {
                if (MessageBox.Show($"确定删除？\n{SelectedQA.Question}", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    _db.DeleteQA(SelectedQA.Id);
                    RefreshList();
                    Clear();
                }
            }
        }

        private void Clear()
        {
            SelectedQA = null;
            EditingQuestion = "";
            EditingAnswer = "";
            EditingPriority = 0;
            EditingPlatform = "WeChat";
        }

        // ==========================================
        //  实现导出功能
        // ==========================================
        private void ExportExcel()
        {
            if (QAList.Count == 0)
            {
                MessageBox.Show("当前列表没有数据，无需导出。");
                return;
            }

            SaveFileDialog sfd = new SaveFileDialog
            {
                Title = "导出问答库",
                Filter = "Excel 文件|*.xlsx",
                FileName = $"QA_Library_{DateTime.Now:yyyyMMdd}.xlsx"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    // 使用 MiniExcel 导出当前列表数据
                    // 它可以直接把 ObservableCollection<QuestionAnswer> 导出，列名即属性名
                    MiniExcel.SaveAs(sfd.FileName, QAList);
                    MessageBox.Show($"导出成功！\n路径: {sfd.FileName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败: {ex.Message}\n请确保文件未被占用。");
                }
            }
        }

        // ==========================================
        //  实现导入功能
        // ==========================================
        private void ImportExcel()
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "导入问答库 (Excel)",
                Filter = "Excel 文件|*.xlsx"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    // 读取 Excel 数据
                    var rows = MiniExcel.Query<QuestionAnswer>(ofd.FileName).ToList();

                    if (rows.Count == 0)
                    {
                        MessageBox.Show("Excel 文件为空或格式不正确。\n请确保表头包含: Question, Answer, Platform, Priority");
                        return;
                    }

                    int count = 0;
                    foreach (var row in rows)
                    {
                        // 简单的校验
                        if (!string.IsNullOrWhiteSpace(row.Question) && !string.IsNullOrWhiteSpace(row.Answer))
                        {
                            // 如果导入的数据没有平台，默认为 WeChat
                            if (string.IsNullOrEmpty(row.Platform)) row.Platform = "WeChat";

                            // 保存到数据库 (注意：这里全是新增，如果需要去重逻辑需额外处理)
                            // 为了避免 Id 冲突，MiniExcel 读取时的 Id 应该忽略，让数据库自增
                            row.Id = 0;
                            _db.SaveQA(row);
                            count++;
                        }
                    }

                    RefreshList();
                    MessageBox.Show($"成功导入 {count} 条问答数据！");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}\n请检查 Excel 格式是否与导出格式一致。");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // 修复后的 RelayCommand (包含 CanExecuteChanged)
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}