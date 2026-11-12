using System;
using System.Collections.Generic;
using System.IO;

namespace DownloadOrganizer
{
    internal static class LocalSearchSmoke
    {
        public static int Main()
        {
            string temp = Path.Combine(Path.GetTempPath(), "LocalSearchSmoke-" + Guid.NewGuid().ToString("N"));
            try
            {
                string root = Path.Combine(temp, "sorted");
                string source = Path.Combine(temp, "downloads");
                Directory.CreateDirectory(source);
                foreach (string name in new string[] { "budget", "meeting", "archive" })
                {
                    string folder = Path.Combine(root, name);
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "report_old.txt"), name);
                }
                var folders = Engine.Folders(root);
                LocalSearchIndex index = new LocalSearchIndex(folders);
                var evidence = index.Score("budget report");
                if (evidence[Path.Combine(root, "budget")].KeywordScore <=
                    evidence[Path.Combine(root, "meeting")].KeywordScore)
                    throw new Exception("드문 키워드의 BM25 우선순위가 작동하지 않습니다.");

                var idfFolders = new List<FolderChoice> {
                    new FolderChoice { Path = Path.Combine(root, "common"), Label = "common" },
                    new FolderChoice { Path = Path.Combine(root, "rare"), Label = "rare" },
                    new FolderChoice { Path = Path.Combine(root, "other"), Label = "other",
                        SampleNames = new List<string> { "common" } }
                };
                var idfEvidence = new LocalSearchIndex(idfFolders).Score("common rare");
                if (idfEvidence[Path.Combine(root, "rare")].KeywordScore <=
                    idfEvidence[Path.Combine(root, "common")].KeywordScore)
                    throw new Exception("여러 폴더에 반복되는 단어보다 드문 단어가 높게 평가되지 않습니다.");

                string exactPath = Path.Combine(source, "budget_summary.txt");
                File.WriteAllText(exactPath, "sample");
                Suggestion exact = Engine.Suggest(new FileInfo(exactPath), folders, index);
                if (!exact.Certain || Path.GetFileName(exact.Folder) != "budget")
                    throw new Exception("명확한 키워드의 자동 추천이 실패했습니다.");

                string typoPath = Path.Combine(source, "budgt.txt");
                File.WriteAllText(typoPath, "sample");
                Suggestion typo = Engine.Suggest(new FileInfo(typoPath), folders, index);
                if (typo.Certain || Path.GetFileName(typo.Folder) != "budget" ||
                    !typo.ReviewState.Contains("오타"))
                    throw new Exception("오타 후보가 안전한 수동 확인 대상으로 표시되지 않습니다.");

                string genericPath = Path.Combine(source, "report.txt");
                File.WriteAllText(genericPath, "sample");
                Suggestion generic = Engine.Suggest(new FileInfo(genericPath), folders, index);
                if (generic.Certain)
                    throw new Exception("여러 폴더에 같은 키워드가 있을 때 자동 이동했습니다.");

                string selfPath = Path.Combine(root, "archive", "unique_sample.txt");
                File.WriteAllText(selfPath, "sample");
                var sameFolderIndex = new LocalSearchIndex(Engine.Folders(root));
                var selfEvidence = sameFolderIndex.Score("unique_sample", Path.Combine(root, "archive"));
                if (selfEvidence[Path.Combine(root, "archive")].KeywordScore > 0)
                    throw new Exception("현재 파일의 이름을 자기 자신의 검색 근거로 사용했습니다.");

                Console.WriteLine("PASS: 오프라인 BM25 키워드 순위, 오타 후보, 자기 파일 제외, 안전한 자동 이동 보류");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
            finally
            {
                if (temp.StartsWith(Path.GetFullPath(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase) && Directory.Exists(temp))
                    Directory.Delete(temp, true);
            }
        }
    }
}
