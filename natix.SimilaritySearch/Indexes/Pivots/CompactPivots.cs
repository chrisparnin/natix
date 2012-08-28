//
//  Copyright 2012  Eric Sadit Tellez Avila
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;

using natix.CompactDS;
using natix.SortingSearching;

namespace natix.SimilaritySearch
{
	public class CompactPivots : BasicIndex
	{
		public IRankSelectSeq[] SEQ;
		public MetricDB PIVS;
		//IList<float> MEAN;
		public IList<float> STDDEV;
		public int SEARCHPIVS;

		public CompactPivots ()
		{
		}

		public override void Load (BinaryReader Input)
		{
			base.Load (Input);
			this.SEARCHPIVS = Input.ReadInt32 ();
			this.PIVS = SpaceGenericIO.SmartLoad(Input, false);
			this.SEQ = new IRankSelectSeq[this.PIVS.Count];
			for (int i = 0; i < this.PIVS.Count; ++i) {
				this.SEQ[i] = RankSelectSeqGenericIO.Load(Input);
			}
			// this.MEAN = new float[this.PIVS.Count];
			this.STDDEV = new float[this.PIVS.Count];
			//PrimitiveIO<float>.ReadFromFile(Input, this.MEAN.Count, this.MEAN);
			PrimitiveIO<float>.ReadFromFile(Input, this.STDDEV.Count, this.STDDEV);
		}

		public override void Save (BinaryWriter Output)
		{
			base.Save (Output);
			Output.Write((int)this.SEARCHPIVS);
			SpaceGenericIO.SmartSave (Output, this.PIVS);
			for (int i = 0; i < this.PIVS.Count; ++i) {
				RankSelectSeqGenericIO.Save (Output, this.SEQ[i]);
			}
			// PrimitiveIO<float>.WriteVector(Output, this.MEAN);
			PrimitiveIO<float>.WriteVector(Output, this.STDDEV);
		}

		public void Build (CompactPivots idx, int num_pivs, int search_pivs, SequenceBuilder seq_build = null)
		{
			this.DB = idx.DB;
			var P = (idx.PIVS as SampleSpace);
			var S = new int[num_pivs];
			this.SEARCHPIVS = search_pivs;
			this.STDDEV = new float[num_pivs];
			this.SEQ = new IRankSelectSeq[num_pivs];
			for (int i = 0; i < num_pivs; ++i) {
				S[i] = P.SAMPLE[i];
				this.STDDEV[i] = idx.STDDEV[i];
				this.SEQ[i] = idx.SEQ[i];
				if (seq_build != null) {
					var seq = this.SEQ[i];
					var _seq = new int[seq.Count];
					// this construction supposes a fast Select operation rather than a fast access
					for (int s = 0; s < seq.Sigma; ++s) {
						var rs = seq.Unravel(s);
						var count1 = rs.Count1;
						for (int c = 1; c <= count1; ++c) {
							_seq[ rs.Select1(c) ] = s;
						}
					}
					this.SEQ[i] = seq_build(_seq, seq.Sigma);
				}
			}
			this.PIVS = new SampleSpace("", P.DB, S);
		}

		public void Build (MetricDB db, int num_pivs, int search_pivs, SequenceBuilder seq_builder = null)
		{
			if (seq_builder == null) {
				// seq_builder = SequenceBuilders.GetSeqXLB_SArray64(24);
				// seq_builder = SequenceBuilders.GetIISeq(BitmapBuilders.GetSArray(BitmapBuilders.GetGGMN_wt(8)));
				seq_builder = SequenceBuilders.GetIISeq(BitmapBuilders.GetSArray(BitmapBuilders.GetDArray_wt (12, 64)));
				// seq_builder = SequenceBuilders.GetIISeq(BitmapBuilders.GetDiffSetRL2(31));
				// seq_builder = SequenceBuilders.GetSeqXLB_DiffSet64(24, 31);
				// seq_builder = SequenceBuilders.GetWT_GGMN_BinaryCoding(12);
				// seq_builder = SequenceBuilders.GetWT_BinaryCoding(BitmapBuilders.GetRRR_wt(12));
			}
			this.DB = db;
			this.PIVS = new SampleSpace("", db, num_pivs);
			this.SEARCHPIVS = search_pivs;
			var n = db.Count;
			// this.MEAN = new float[num_pivs];
			this.STDDEV = new float[num_pivs];
			this.SEQ = new IRankSelectSeq[num_pivs];
			var seq = new float[n];
			var S = new int[n];
			for (int i = 0; i < num_pivs; ++i) {
				for (int j = 0; j < n; ++j) {
					var d = this.DB.Dist (this.PIVS[i], this.DB [j]);
					seq[j]  = (float)d;
				}
				int sigma = 0;
				this.ComputeStats(seq, i);
				for (int j = 0; j < n; ++j) {
					var sym = this.Discretize (seq[j], this.STDDEV[i]);
					S[j] = sym;
					sigma = Math.Max (sigma, sym);
				}
				this.SEQ[i] = seq_builder(S, sigma + 1);
				if (i % 10 == 0) {
					Console.WriteLine ("XXX advance: {0}/{1}, sigma: {2}", i, num_pivs, sigma+1);
				}
			}
		}

		public virtual int Discretize (double d, float stddev)
		{
			var sym = d / stddev;
			if (ushort.MaxValue < sym) {
				// we should have really small values, other values are problems induced by Result.MaxValue
				sym = ushort.MaxValue;
			}
			/*if (sym >= 4) {
				--sym;
			}*/
			return (int)sym;
		}

		protected void ComputeStats(float[] seq, int piv_id)
		{
			float mean = 0;
			float stddev = 0;
			for (int i = 0; i < seq.Length; ++i) {
				mean += seq[i];
			}
			mean = mean / seq.Length;
			for (int i = 0; i < seq.Length; ++i) {
				float x = seq[i] - mean;
				stddev += x * x;
			}
			stddev = (float)Math.Sqrt(stddev / seq.Length);
			// this.MEAN[piv_id] = mean;
			this.STDDEV[piv_id] = stddev;
		}

		public override IResult SearchKNN (object q, int K, IResult res)
		{
			var m = this.PIVS.Count;
			var max = Math.Min (this.SEARCHPIVS, m);
			var P = new TopK<Tuple<double, float, IRankSelectSeq>> (max);
			var A = new byte[this.DB.Count];
			var _PIVS = (this.PIVS as SampleSpace).SAMPLE;
			for (int piv_id = 0; piv_id < m; ++piv_id) {
				var stddev = this.STDDEV [piv_id];
				var dqp = this.DB.Dist (q, this.PIVS [piv_id]);
				var seq = this.SEQ [piv_id];
				A[_PIVS[piv_id]] = (byte)max;
				res.Push(_PIVS[piv_id], dqp);
				var start_sym = Math.Max (this.Discretize (dqp, stddev), 0);
				var end_sym = this.Discretize (dqp, stddev);
				var count = Math.Min(start_sym, Math.Abs(seq.Sigma - 1 - end_sym));
				P.Push (count, Tuple.Create (dqp, stddev, seq));
			}
			var queue = new Queue<IEnumerator<IRankSelect>> ();
			foreach (var p in P.Items.Traverse()) {
				var tuple = p.Value;
				var it = this.IteratePartsKNN(res, tuple.Item1, tuple.Item2, tuple.Item3).GetEnumerator();
				if (it.MoveNext()) {
					queue.Enqueue(it);
				}
			}
			while (queue.Count > 0) {
				var L = queue.Dequeue();
				var rs = L.Current;
				var count1 = rs.Count1;
				// Console.WriteLine ("queue-count: {0}", queue.Count);
				for (int i = 1; i <= count1; ++i) {
					var item = rs.Select1 (i);
					A [item]++;
					if (A [item] == max) {
						var dist = this.DB.Dist (q, this.DB [item]);
						res.Push (item, dist);
					}
				}
				if (L.MoveNext ()) {
					queue.Enqueue (L);
				}
			}
			return res;
		}

		public IEnumerable<IRankSelect> IteratePartsKNN (IResult res, double dqp, float stddev, IRankSelectSeq seq)
		{
			var sym = this.Discretize(dqp, stddev);
			yield return seq.Unravel(sym);
			var left = sym - 1;
			var right = sym + 1;
			bool do_next = true;
			while (do_next) {
				do_next = false;
				var __left = this.Discretize(dqp - res.CoveringRadius, stddev);
				if (0 <= left && __left <= left) {
					yield return seq.Unravel(left);
					--left;
					do_next = true;
				}
				var __right = this.Discretize(dqp + res.CoveringRadius, stddev);
				if (right <= __right && right < seq.Sigma) {
					yield return seq.Unravel(right);
					++right;
					do_next = true;
				}
				/*Console.WriteLine ("left: {0}, right: {1}, __left: {2}, __right: {3}",
				                   left, right, __left, __right);*/
			}
		}

		public override IResult SearchRange (object q, double radius)
		{
			var m = this.PIVS.Count;
			var P = new TopK<Tuple<double, int, int, IRankSelectSeq>> (this.SEARCHPIVS);
			for (int piv_id = 0; piv_id < m; ++piv_id) {
				var dqp = this.DB.Dist (q, this.PIVS [piv_id]);
				var stddev = this.STDDEV [piv_id];
				var start_sym = Math.Max (this.Discretize (dqp - radius, stddev), 0);
				var seq = this.SEQ [piv_id];
				var end_sym = Math.Min (this.Discretize (dqp + radius, stddev), seq.Sigma - 1);
				var count = 0;
				var n = seq.Count;
				for (int s = start_sym; s <= end_sym; ++s) {
					count += seq.Rank (s, n - 1);
				}
				P.Push (count, Tuple.Create (dqp, start_sym, end_sym, seq));
			}
			HashSet<int> A = new HashSet<int>();
			HashSet<int> B = null;
			int I = 0;
			foreach (var p in P.Items.Traverse()) {
				var tuple = p.Value;
				// var dpq = tuple.Item1;
				var start_sym = tuple.Item2;
				var end_sym = tuple.Item3;
				var seq = tuple.Item4;
				for (int s = start_sym; s <= end_sym; ++s) {
					var rs = seq.Unravel(s);
					var count1 = rs.Count1;
					for (int i = 1; i < count1; ++i) {
						if (B == null) {
							A.Add( rs.Select1(i) );
						} else {
							var pos = rs.Select1(i);
							if (A.Contains(pos)) {
								B.Add( pos );
							}
						}
					}
				}
				if (B == null) {
					B = new HashSet<int>();
				} else {
					A = B;
					B = new HashSet<int>();
				}
				++I;
			}
			// Console.WriteLine();
			B = null;
			var res = new Result(this.DB.Count, false);
			foreach (var docid in A) {
				var d = this.DB.Dist(this.DB[docid], q);
				if (d <= radius) {
					res.Push(docid, d);
				}
			}
			return res;
		}
	}
}

