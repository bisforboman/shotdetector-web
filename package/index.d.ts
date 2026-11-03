// Types of shotdetector-web's public API (index.js; decision docs/decisions/2026-11-02-npm-package.md).

export type DetectorKind = 'adaptive' | 'content' | 'threshold' | 'histogram' | 'hash';

/** A run's options, as ShotDetector takes them; each left out (or null) is ShotDetector's default. */
export interface DetectOptions {
  detector?: DetectorKind | null;
  /** The detector's threshold. */
  threshold?: number | null;
  /** The shortest shot, in scenedetect's forms: frames ("15"), seconds ("0.6s") or a timecode. */
  minSceneLength?: string | null;
  /** The adaptive detector's window: frames on each side averaged. */
  frameWindow?: number | null;
  /** The adaptive detector's minimum content_val for a cut. */
  minContentVal?: number | null;
  deinterlace?: 'off' | 'auto' | 'on' | null;
  /** Several detectors in one run, instead of `detector`. */
  detectors?: { kind: DetectorKind; threshold?: number | null }[] | null;
  onProgress?: (progress: { done: number; expected: number }) => void;
  /** Each shot as soon as ShotDetector knows it. */
  onShot?: (shot: Shot) => void;
  /** A paused run has come to be held: the frames read, where a stop now ends it. */
  onPaused?: (paused: { frames: number }) => void;
  /** Cancels the run: its promise rejects with an AbortError. */
  signal?: AbortSignal;
}

export interface DetectorSettings {
  /** Where the package's files are served from (default: this module's folder). */
  base?: string | URL;
  /** The bundle: 'aot' (default; faster, larger) or 'interpreted' (smaller, slower). */
  bundle?: 'aot' | 'interpreted';
  /** Decoder threads (more than 1: the threaded AOT bundle, only where the page is crossOriginIsolated). */
  threads?: number;
}

/** One shot as ShotDetector's Shots.Json writes it. */
export interface Shot {
  shot: number;
  startFrame: number;
  endFrame: number;
  startTimecode: string;
  endTimecode: string;
  durationFrames: number;
  durationSeconds: number;
}

export interface Rational { num: number; den: number }

export interface DetectResult {
  shots: Shot[];
  /** The frames read: the file's, or up to where a stopped run ended. */
  frames: number;
  frameRate: Rational;
  /** The file's duration in seconds. */
  duration: number | null;
  stopped: boolean;
  /** ShotDetector's warnings on how the video was read. */
  warnings: string[];
  /** The decoder's threads the run had (1 where threads weren't usable). */
  threads: number;
}

/** A run: a promise of its result, with controls. */
export interface Run<T> extends Promise<T> {
  /** Ends the run at its next pause: the result for the frames read, stopped: true. */
  stop(): void;
  pause(): void;
  resume(): void;
  /** Abandons the run: the promise rejects with an AbortError. */
  cancel(): void;
}

/** The opened file as FFmpeg describes it (Open's JSON). */
export type Video = Record<string, unknown> & {
  width: number;
  height: number;
  frameRate: Rational;
  frameCount: number | null;
  frames: number;
};

export type TimelineFormat = 'edl' | 'fcpxml' | 'fcp7' | 'otio' | 'qp' | 'keyframes' | 'shotchanges';

export interface Chapters { minimum: number; starts: number[]; text: string; notMade: string | null }

export class ShotDetector {
  constructor(settings?: DetectorSettings);
  open(file: Blob): Promise<Video>;
  detect(options?: DetectOptions): Run<DetectResult>;
  scenesCsv(): Promise<string>;
  statsCsv(): Promise<Blob>;
  shotsJson(): Promise<string>;
  shotsHtml(): Promise<string>;
  timeline(format: TimelineFormat, fileName?: string): Promise<string>;
  chapters(minimum?: number): Promise<Chapters>;
  stills(shots: Shot[], options?: { at?: 'middle' | 'first'; width?: number; quality?: number }): Promise<(Blob | null)[]>;
  close(): void;
}

export type Output = 'scenesCsv' | 'statsCsv' | 'shotsJson' | 'shotsHtml' | 'chapters' | TimelineFormat;

export interface DetectShotsOptions extends DetectOptions, DetectorSettings {
  /** A still per shot ('middle', default, or 'first'), or none (false). */
  stills?: 'middle' | 'first' | false;
  stillWidth?: number;
  /** More of the run to fetch before the worker ends. */
  outputs?: Output[];
}

export interface DetectShotsResult extends DetectResult {
  video: Video;
  stills: (Blob | null)[] | null;
  files: Partial<Record<Output, string | Blob | Chapters>>;
}

export function detectShots(file: Blob, options?: DetectShotsOptions): Run<DetectShotsResult>;
