// Ported from the Sonic library by Bill Cox (https://github.com/waywardgeek/sonic, Sonic.java),
// with fixes from the Android Open Source Project's copy in androidx/media (Sonic.java).
//
// Copyright 2010, 2011 Bill Cox
// Copyright (C) 2017 The Android Open Source Project
// Modifications copyright IdleMad Studio
//
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software distributed under the License is
// distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and limitations under the License.
//
// Changes from the original: speed change only (no pitch, rate, volume or chord-pitch paths), mono float
// samples, a speed that may change between writes, the androidx/media rounding-error carry and end-of-stream
// count, and the buffer counts SpeechStream needs to map output samples back to source samples.

namespace Hearwood.Audio;

/// <summary>
/// Sonic's speed change for mono speech: changes how fast speech plays without changing its pitch, by
/// finding each pitch period (AMDF) and dropping or repeating whole periods with a crossfade. Made for
/// speeding speech up, where it stays clear past 2×. Write samples in, read them out; <see cref="Speed"/>
/// may change between writes.
/// </summary>
public sealed class Sonic
{
    const int MinPitchHz = 65;
    const int MaxPitchHz = 400;
    /// <summary>The pitch search first runs on input averaged down to about this rate, then refines.</summary>
    const int AmdfHz = 4000;

    readonly int _skip;
    readonly int _minPeriod;
    readonly int _maxPeriod;
    readonly int _maxRequired;
    readonly float[] _downSampled;

    float[] _input;
    float[] _output;
    int _inputCount;
    int _outputCount;
    int _remainingInputToCopy;
    int _prevPeriod;
    double _minDiff;
    double _maxDiff;
    double _prevMinDiff;
    double _speedError;

    public Sonic(int sampleRate)
    {
        _skip = sampleRate > AmdfHz ? sampleRate / AmdfHz : 1;
        _minPeriod = sampleRate / MaxPitchHz;
        _maxPeriod = sampleRate / MinPitchHz;
        _maxRequired = 2 * _maxPeriod;
        _downSampled = new float[_maxRequired];
        _input = new float[_maxRequired];
        _output = new float[_maxRequired];
    }

    /// <summary>Playback speed: 2 plays twice as fast. Applies to the input not yet processed.</summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>Input written but not yet turned into output.</summary>
    public int UnprocessedSamples => _inputCount;

    /// <summary>Output ready to read.</summary>
    public int AvailableSamples => _outputCount;

    /// <summary>Adds samples and processes as many pitch periods as they complete.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        EnsureInput(samples.Length);
        samples.CopyTo(_input.AsSpan(_inputCount));
        _inputCount += samples.Length;
        Process();
    }

    /// <summary>Takes up to <c>buffer.Length</c> output samples; returns how many.</summary>
    public int Read(Span<float> buffer)
    {
        int n = Math.Min(buffer.Length, _outputCount);
        _output.AsSpan(0, n).CopyTo(buffer);
        _output.AsSpan(n, _outputCount - n).CopyTo(_output);
        _outputCount -= n;
        return n;
    }

    /// <summary>
    /// Turns all remaining input into output, at the current speed. No delay is added, but flushing in the
    /// middle of a word can distort it, so flush only at the end.
    /// </summary>
    public void Flush()
    {
        int remaining = _inputCount;
        // Input still to be copied straight through comes out at 1×.
        int stretched = remaining - _remainingInputToCopy;
        int expected = _outputCount + (int)(stretched / Speed + _remainingInputToCopy + _speedError + 0.5);
        _speedError = 0;

        // Silence pushes the last periods through; what it adds beyond the expected count is cut.
        EnsureInput(2 * _maxRequired);
        _input.AsSpan(_inputCount, 2 * _maxRequired).Clear();
        _inputCount += 2 * _maxRequired;
        Process();
        if (_outputCount > expected)
            _outputCount = Math.Max(expected, 0);
        _inputCount = 0;
        _remainingInputToCopy = 0;
    }

    void Process()
    {
        double speed = Speed;
        if (speed > 1.00001 || speed < 0.99999)
            ChangeSpeed(speed);
        else
        {
            CopyToOutput(0, _inputCount);
            _inputCount = 0;
        }
    }

    void ChangeSpeed(double speed)
    {
        if (_inputCount < _maxRequired)
            return;
        int position = 0;
        do
        {
            if (_remainingInputToCopy > 0)
                position += CopyInputToOutput(position);
            else
            {
                int period = FindPitchPeriod(position);
                position += speed > 1.0
                    ? period + SkipPitchPeriod(position, speed, period)
                    : InsertPitchPeriod(position, speed, period);
            }
        }
        while (position + _maxRequired <= _inputCount);
        _input.AsSpan(position, _inputCount - position).CopyTo(_input);
        _inputCount -= position;
    }

    int CopyInputToOutput(int position)
    {
        int n = Math.Min(_maxRequired, _remainingInputToCopy);
        CopyToOutput(position, n);
        _remainingInputToCopy -= n;
        return n;
    }

    void CopyToOutput(int position, int count)
    {
        EnsureOutput(count);
        _input.AsSpan(position, count).CopyTo(_output.AsSpan(_outputCount));
        _outputCount += count;
    }

    /// <summary>Drops a pitch period: crossfades it into the next, then copies some input straight through.</summary>
    int SkipPitchPeriod(int position, double speed, int period)
    {
        int newSamples;
        if (speed >= 2.0)
        {
            double expected = period / (speed - 1.0) + _speedError;
            newSamples = (int)Math.Round(expected);
            _speedError = expected - newSamples;
        }
        else
        {
            newSamples = period;
            double expectedCopy = period * (2.0 - speed) / (speed - 1.0) + _speedError;
            _remainingInputToCopy = (int)Math.Round(expectedCopy);
            _speedError = expectedCopy - _remainingInputToCopy;
        }
        EnsureOutput(newSamples);
        OverlapAdd(newSamples, _outputCount, position, position + period);
        _outputCount += newSamples;
        return newSamples;
    }

    /// <summary>Repeats a pitch period: copies it, crossfades it back into itself, then copies some input through.</summary>
    int InsertPitchPeriod(int position, double speed, int period)
    {
        int newSamples;
        if (speed < 0.5)
        {
            double expected = period * speed / (1.0 - speed) + _speedError;
            newSamples = (int)Math.Round(expected);
            _speedError = expected - newSamples;
        }
        else
        {
            newSamples = period;
            double expectedCopy = period * (2.0 * speed - 1.0) / (1.0 - speed) + _speedError;
            _remainingInputToCopy = (int)Math.Round(expectedCopy);
            _speedError = expectedCopy - _remainingInputToCopy;
        }
        EnsureOutput(period + newSamples);
        _input.AsSpan(position, period).CopyTo(_output.AsSpan(_outputCount));
        OverlapAdd(newSamples, _outputCount + period, position + period, position);
        _outputCount += period + newSamples;
        return newSamples;
    }

    /// <summary>Writes <paramref name="count"/> samples that fade from the input at <paramref name="down"/> to the input at <paramref name="up"/>.</summary>
    void OverlapAdd(int count, int outAt, int down, int up)
    {
        for (int t = 0; t < count; t++)
            _output[outAt + t] = (_input[down + t] * (count - t) + _input[up + t] * t) / count;
    }

    /// <summary>
    /// The pitch period at <paramref name="position"/>: searched on input averaged down to about 4 kHz, then
    /// refined at the full rate. At the abrupt end of a voiced word the previous period can be the better match.
    /// </summary>
    int FindPitchPeriod(int position)
    {
        int period;
        if (_skip == 1)
            period = FindPitchPeriodInRange(_input, position, _minPeriod, _maxPeriod);
        else
        {
            DownSample(position);
            period = _skip * FindPitchPeriodInRange(_downSampled, 0, _minPeriod / _skip, _maxPeriod / _skip);
            int min = Math.Max(period - 4 * _skip, _minPeriod);
            int max = Math.Min(period + 4 * _skip, _maxPeriod);
            period = FindPitchPeriodInRange(_input, position, min, max);
        }
        int chosen = PreviousPeriodBetter() ? _prevPeriod : period;
        _prevMinDiff = _minDiff;
        _prevPeriod = period;
        return chosen;
    }

    void DownSample(int position)
    {
        int count = _maxRequired / _skip;
        for (int i = 0; i < count; i++)
        {
            double sum = 0;
            for (int j = 0; j < _skip; j++)
                sum += _input[position + i * _skip + j];
            _downSampled[i] = (float)(sum / _skip);
        }
    }

    /// <summary>The period with the least average magnitude difference between a span and the next (AMDF).</summary>
    int FindPitchPeriodInRange(float[] samples, int position, int minPeriod, int maxPeriod)
    {
        int bestPeriod = 0, worstPeriod = 255;
        double minDiff = 1, maxDiff = 0;
        for (int period = minPeriod; period <= maxPeriod; period++)
        {
            double diff = 0;
            for (int i = 0; i < period; i++)
                diff += Math.Abs(samples[position + i] - samples[position + period + i]);
            if (diff * bestPeriod < minDiff * period)
            {
                minDiff = diff;
                bestPeriod = period;
            }
            if (diff * worstPeriod > maxDiff * period)
            {
                maxDiff = diff;
                worstPeriod = period;
            }
        }
        _minDiff = minDiff / bestPeriod;
        _maxDiff = maxDiff / worstPeriod;
        return bestPeriod;
    }

    bool PreviousPeriodBetter()
    {
        if (_minDiff == 0 || _prevPeriod == 0)
            return false;
        if (_maxDiff > _minDiff * 3)
            return false; // A clear match this period.
        if (_minDiff * 2 <= _prevMinDiff * 3)
            return false; // Not much worse than last period's match.
        return true;
    }

    void EnsureInput(int more) => Grow(ref _input, _inputCount + more);

    void EnsureOutput(int more) => Grow(ref _output, _outputCount + more);

    static void Grow(ref float[] buffer, int needed)
    {
        if (needed > buffer.Length)
            Array.Resize(ref buffer, Math.Max(needed, buffer.Length + buffer.Length / 2));
    }
}
