using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DownloadOrganizer
{
    internal sealed class TopicCandidate
    {
        public string Line = "";          // "주제" or "상위 > 하위", as written into the topic list
        public int FileCount;             // files whose name or folder contains the topic words
        public bool MatchesExistingFolder;
        public List<string> Examples = new List<string>();
    }

    // Suggests topic lines from watch-folder subfolder names and file names only.
    // Local frequent-phrase mining: words, adjacent word pairs and shared Hangul prefixes
    // (for compounds such as 기관업무혁신시스템 / 기관업무혁신계획) are counted per file,
    // then a greedy coverage pass keeps topics that each add enough new files.
    internal static class TopicSuggester
    {
        private const int MaximumTopics = 12;
        private const int MaximumSubtopics = 4;

        private static readonly HashSet<string> Stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "자료", "문서", "파일", "다운로드", "기타", "최종", "최종본", "사본", "복사본", "새", "첨부", "붙임",
            "별첨", "개정", "수정", "수정본", "제출본", "버전", "초안", "참고", "관련", "및", "등", "the", "and",
            "of", "for", "new", "file", "download", "copy", "final", "draft", "ver", "rev", "www", "http",
            "https", "com", "net", "org", "co", "kr", "go", "ac", "or", "re", "html", "htm", "pdf", "hwp",
            "hwpx", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "json", "csv", "zip", "img", "image"
        };
        private static readonly Regex WordPattern = new Regex(@"[\p{L}\p{Nd}]+", RegexOptions.Compiled);
        private static readonly Regex NoiseWord = new Regex(
            @"^(\d+|\d+(학년도|년도|년|월|일|시|분|초|차|회|번|호|부|권|쪽|개|명|건|분기|학기|기|p|px|x)|v\d+|\d+[a-z])$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal sealed class Token { public string Key, Display; public int Segment; }

        private sealed class Candidate
        {
            public string Key = "", Display = "";
            public HashSet<int> Files = new HashSet<int>();
            public double Weight = 1;
            public double Score { get { return Files.Count * Weight; } }
        }

        internal static List<Token> Tokens(string text)
        {
            List<Token> result = new List<Token>();
            foreach (Match match in WordPattern.Matches(text.Normalize(NormalizationForm.FormKC)))
            {
                string display = match.Value, key = display.ToLowerInvariant();
                if (key.Length < 2 || Stop.Contains(key) || NoiseWord.IsMatch(key)) continue;
                result.Add(new Token { Key = key, Display = display });
            }
            return result;
        }

        private static bool IsHangul(string value)
        {
            return value.All(c => c >= '가' && c <= '힣');
        }

        private static void AddCandidate(Dictionary<string, Candidate> candidates, string key,
            string display, int file, double weight)
        {
            Candidate candidate;
            if (!candidates.TryGetValue(key, out candidate))
            {
                candidate = new Candidate { Key = key, Display = display, Weight = weight };
                candidates.Add(key, candidate);
            }
            candidate.Files.Add(file);
        }

        private static List<Candidate> Mine(List<List<Token>> documents, IEnumerable<int> scope,
            int minimumSupport, HashSet<string> excluded)
        {
            Dictionary<string, Candidate> candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, HashSet<string>> tokensByPrefix = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            List<int> files = scope.ToList();
            foreach (int file in files)
            {
                List<Token> tokens = documents[file];
                for (int i = 0; i < tokens.Count; i++)
                {
                    Token token = tokens[i];
                    if (excluded.Contains(token.Key)) continue;
                    AddCandidate(candidates, token.Key, token.Display, file,
                        1.0 + 0.08 * Math.Min(6, token.Key.Length - 2));
                    // Word pairs stay within one name: never join a file name to a folder name
                    // or one folder level to the next.
                    if (i + 1 < tokens.Count && tokens[i + 1].Segment == token.Segment &&
                        !excluded.Contains(tokens[i + 1].Key) && tokens[i + 1].Key != token.Key)
                        AddCandidate(candidates, token.Key + " " + tokens[i + 1].Key,
                            token.Display + " " + tokens[i + 1].Display, file, 1.35);
                    if (IsHangul(token.Key) && token.Key.Length >= 3)
                        for (int length = 3; length <= token.Key.Length; length++)
                        {
                            string prefix = token.Key.Substring(0, length);
                            HashSet<string> owners;
                            if (!tokensByPrefix.TryGetValue(prefix, out owners))
                                tokensByPrefix.Add(prefix, owners = new HashSet<string>());
                            owners.Add(token.Key);
                            AddCandidate(candidates, "\u0001" + prefix, prefix, file,
                                1.0 + 0.08 * Math.Min(6, length - 2));
                        }
                }
            }
            // A prefix only counts when it joins at least two different compound words.
            foreach (string key in candidates.Keys.Where(k => k.StartsWith("\u0001")).ToList())
            {
                string prefix = key.Substring(1);
                Candidate whole;
                if (tokensByPrefix[prefix].Count < 2 ||
                    (candidates.TryGetValue(prefix, out whole) && whole.Files.SetEquals(candidates[key].Files)))
                    candidates.Remove(key);
            }
            int ceiling = files.Count < 10 ? files.Count : (int)Math.Ceiling(files.Count * 0.6);
            List<Candidate> ranked = candidates.Values.Where(c => c.Files.Count >= minimumSupport &&
                c.Files.Count <= ceiling).ToList();
            // Of two candidates covering the same files, keep the longer, more specific one.
            ranked = ranked.Where(c => !ranked.Any(o => o != c && o.Display.Length > c.Display.Length &&
                o.Key.TrimStart('\u0001').Contains(c.Key.TrimStart('\u0001')) &&
                o.Files.SetEquals(c.Files))).ToList();
            return ranked.OrderByDescending(c => c.Score).ThenBy(c => c.Display, StringComparer.CurrentCulture).ToList();
        }

        private static List<Candidate> Cover(List<Candidate> ranked, int minimumSupport, int maximum)
        {
            List<Candidate> picked = new List<Candidate>();
            HashSet<int> covered = new HashSet<int>();
            List<Candidate> pool = ranked.ToList();
            while (picked.Count < maximum && pool.Count > 0)
            {
                Candidate best = null; double bestGain = 0;
                foreach (Candidate candidate in pool)
                {
                    int fresh = candidate.Files.Count(f => !covered.Contains(f));
                    if (fresh < minimumSupport || fresh < candidate.Files.Count * 0.5) continue;
                    double gain = fresh * candidate.Weight;
                    if (gain > bestGain) { best = candidate; bestGain = gain; }
                }
                if (best == null) break;
                picked.Add(best); pool.Remove(best);
                covered.UnionWith(best.Files);
            }
            return picked;
        }

        private static string NameKey(string name)
        {
            return String.Join(" ", Tokens(name).Select(t => t.Key).ToArray());
        }

        public static List<TopicCandidate> Suggest(string source, IEnumerable<string> files,
            IEnumerable<string> existingFolderNames)
        {
            source = Engine.Full(source);
            List<string> paths = files.Select(Engine.Full).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<List<Token>> documents = new List<List<Token>>();
            foreach (string path in paths)
            {
                List<Token> tokens = Tokens(Path.GetFileNameWithoutExtension(path));
                string folder = Path.GetDirectoryName(path);
                int segment = 0;
                if (Engine.SameOrInside(folder, source) && !folder.Equals(source, StringComparison.OrdinalIgnoreCase))
                    foreach (string part in folder.Substring(source.Length).Split(
                        new char[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        segment++;
                        foreach (Token token in Tokens(part)) { token.Segment = segment; tokens.Add(token); }
                    }
                documents.Add(tokens.GroupBy(t => t.Key).Select(g => g.First()).ToList());
            }
            HashSet<string> existingNames = new HashSet<string>(existingFolderNames.Select(NameKey),
                StringComparer.OrdinalIgnoreCase);

            int support = Math.Max(3, paths.Count / 200);
            List<Candidate> top = Cover(Mine(documents, Enumerable.Range(0, paths.Count), support,
                new HashSet<string>()), support, MaximumTopics);
            List<TopicCandidate> result = new List<TopicCandidate>();
            foreach (Candidate topic in top)
            {
                result.Add(Describe(topic.Display, topic, paths, existingNames));
                if (topic.Files.Count < 12) continue;
                int childSupport = Math.Max(3, topic.Files.Count / 10);
                HashSet<string> parentWords = new HashSet<string>(
                    Tokens(topic.Display).Select(t => t.Key), StringComparer.OrdinalIgnoreCase);
                // A subtopic must narrow its parent, not repeat another top-level topic.
                List<Candidate> children = Cover(Mine(documents, topic.Files, childSupport, parentWords)
                    .Where(c => c.Files.Count < topic.Files.Count * 0.9 &&
                        !top.Any(other => other != topic && (other.Key == c.Key ||
                            c.Files.Count(other.Files.Contains) > c.Files.Count / 2))).ToList(),
                    childSupport, MaximumSubtopics);
                foreach (Candidate child in children)
                    result.Add(Describe(topic.Display + " > " + child.Display, child, paths, existingNames));
            }
            return result;
        }

        private static TopicCandidate Describe(string line, Candidate candidate, List<string> paths,
            HashSet<string> existingNames)
        {
            string leafKey = NameKey(line.Split('>').Last());
            return new TopicCandidate {
                Line = line,
                FileCount = candidate.Files.Count,
                MatchesExistingFolder = leafKey != "" && existingNames.Contains(leafKey),
                Examples = candidate.Files.Take(3).Select(i => Path.GetFileName(paths[i])).ToList()
            };
        }

        // Score used to place a file under a topic folder. A topic whose every word appears
        // inside the file name (also as part of a longer compound) counts as a clear match.
        public static double TopicScore(string stem, string topic)
        {
            double score = Engine.Similarity(stem, topic);
            List<string> topicWords = Tokens(topic).Select(t => t.Key).ToList();
            if (topicWords.Count == 0) return score;
            List<string> stemWords = Tokens(stem).Select(t => t.Key).ToList();
            if (topicWords.All(w => stemWords.Any(s => s.Contains(w)))) score = Math.Max(score, 0.8);
            return score;
        }

        // Picks one topic for a file. Topics are relative paths ("업무\예산"). When a topic and
        // its own subtopic both match, the deeper one wins; otherwise the best match must lead
        // every unrelated topic by a clear margin.
        public static int Pick(IList<string> topicPaths, IList<double> scores)
        {
            const double Threshold = 0.62, Margin = 0.15;
            int best = -1;
            for (int i = 0; i < scores.Count; i++)
                if (scores[i] >= Threshold && (best < 0 || scores[i] > scores[best])) best = i;
            if (best < 0) return -1;
            Func<int, int, bool> inside = (child, parent) =>
                topicPaths[child].StartsWith(topicPaths[parent] + "\\", StringComparison.OrdinalIgnoreCase);
            Func<int, int, bool> related = (a, b) => a == b || inside(a, b) || inside(b, a);
            double chainTop = scores[best];
            for (int deeper = best; deeper >= 0; )
            {
                best = deeper; deeper = -1;
                for (int i = 0; i < scores.Count; i++)
                    if (scores[i] >= Threshold && inside(i, best) &&
                        (deeper < 0 || scores[i] > scores[deeper])) deeper = i;
            }
            double rival = 0;
            for (int i = 0; i < scores.Count; i++)
                if (!related(i, best)) rival = Math.Max(rival, scores[i]);
            return chainTop - rival >= Margin ? best : -1;
        }

        public static string LeafName(string topicPath)
        {
            return topicPath.Split('\\').Last();
        }
    }
}
