using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DownloadOrganizer
{
    internal sealed class SearchEvidence
    {
        internal double KeywordScore;
        internal double FuzzyScore;
        internal string Keyword = "";
        internal string FuzzyWord = "";
    }

    // Built once per folder scan. All ranking happens locally; no network or extra runtime is needed.
    internal sealed class LocalSearchIndex
    {
        private sealed class Document
        {
            internal FolderChoice Folder;
            internal Dictionary<string, int> Terms = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            internal List<string> FolderWords = new List<string>();
            internal int Length;
        }

        private readonly List<Document> documents = new List<Document>();
        private readonly Dictionary<string, int> documentFrequency =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly double averageLength;

        internal LocalSearchIndex(List<FolderChoice> folders)
        {
            foreach (FolderChoice folder in folders)
            {
                Document document = BuildDocument(folder, null);
                foreach (string word in document.Terms.Keys)
                {
                    int count;
                    documentFrequency.TryGetValue(word, out count);
                    documentFrequency[word] = count + 1;
                }
                documents.Add(document);
            }
            averageLength = documents.Count == 0 ? 1 :
                Math.Max(1, documents.Average(d => d.Length));
        }

        private static Document BuildDocument(FolderChoice folder, string excludedStem)
        {
            Document document = new Document { Folder = folder };
            document.FolderWords = Engine.Words(Path.GetFileName(folder.Path));
            foreach (string word in document.FolderWords) Add(document, word, 3);
            string[] pathParts = folder.Label.Split(new char[] { '\\', '/' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < pathParts.Length - 1; i++)
                foreach (string word in Engine.Words(pathParts[i])) Add(document, word, 1);
            foreach (string name in folder.SampleNames)
            {
                if (excludedStem != null && String.Equals(name, excludedStem,
                    StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string word in Engine.Words(name)) Add(document, word, 1);
            }
            document.Length = document.Terms.Values.Sum();
            return document;
        }

        private static void Add(Document document, string word, int weight)
        {
            int count;
            document.Terms.TryGetValue(word, out count);
            document.Terms[word] = Math.Min(8, count + weight);
        }

        private double Idf(string term)
        {
            int count;
            documentFrequency.TryGetValue(term, out count);
            return Math.Log(1.0 + (documents.Count - count + 0.5) / (count + 0.5));
        }

        private static int EditDistanceWithin(string left, string right, int maximum)
        {
            if (Math.Abs(left.Length - right.Length) > maximum) return maximum + 1;
            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int j = 0; j <= right.Length; j++) previous[j] = j;
            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                int rowMinimum = current[0];
                for (int j = 1; j <= right.Length; j++)
                {
                    int substitution = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                        previous[j - 1] + substitution);
                    rowMinimum = Math.Min(rowMinimum, current[j]);
                }
                if (rowMinimum > maximum) return maximum + 1;
                int[] swap = previous; previous = current; current = swap;
            }
            return previous[right.Length];
        }

        private static double FuzzyMatch(List<string> query, List<string> folderWords,
            out string matchedWord)
        {
            double best = 0;
            matchedWord = "";
            foreach (string sought in query)
            {
                if (sought.Length < 4 || sought.Length > 32) continue;
                foreach (string word in folderWords)
                {
                    if (word.Length < 4 || word.Length > 32 || sought == word ||
                        sought[0] != word[0]) continue;
                    int maximum = Math.Min(sought.Length, word.Length) >= 8 ? 2 : 1;
                    int distance = EditDistanceWithin(sought, word, maximum);
                    if (distance > maximum) continue;
                    double score = 1.0 - distance / (double)Math.Max(sought.Length, word.Length);
                    if (score > best) { best = score; matchedWord = word; }
                }
            }
            return best;
        }

        internal Dictionary<string, SearchEvidence> Score(string fileStem, string currentFolder = null)
        {
            List<string> query = Engine.Words(fileStem);
            Dictionary<string, SearchEvidence> result = new Dictionary<string, SearchEvidence>(
                StringComparer.OrdinalIgnoreCase);
            double largest = 0;
            foreach (Document document in documents)
            {
                Document scoringDocument = currentFolder != null &&
                    String.Equals(document.Folder.Path, currentFolder, StringComparison.OrdinalIgnoreCase) ?
                    BuildDocument(document.Folder, fileStem) : document;
                SearchEvidence evidence = new SearchEvidence();
                double keyword = 0;
                foreach (string term in query)
                {
                    int frequency;
                    if (!scoringDocument.Terms.TryGetValue(term, out frequency)) continue;
                    double idf = Idf(term);
                    double norm = 1.2 * (1.0 - 0.75 + 0.75 * scoringDocument.Length / averageLength);
                    keyword += idf * frequency * 2.2 / (frequency + norm);
                    if (evidence.Keyword == "" || idf > Idf(evidence.Keyword))
                        evidence.Keyword = term;
                }
                evidence.KeywordScore = keyword;
                evidence.FuzzyScore = FuzzyMatch(query, document.FolderWords,
                    out evidence.FuzzyWord);
                largest = Math.Max(largest, keyword);
                result[document.Folder.Path] = evidence;
            }
            if (largest > 0)
                foreach (SearchEvidence evidence in result.Values)
                    evidence.KeywordScore /= largest;
            return result;
        }
    }
}
