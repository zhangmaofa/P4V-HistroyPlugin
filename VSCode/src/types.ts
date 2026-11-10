export interface AnnotationLine {
  readonly change: number;
  readonly user: string;
  readonly modifiedAt?: string;
  readonly client: string;
  readonly text: string;
  readonly isLocal: boolean;
}

export interface P4WorkspaceContext {
  readonly client: string;
  readonly user: string;
  readonly connectionAddress: string;
  readonly workingDirectory: string;
}

export interface AnnotationLoadResult {
  readonly workspace?: P4WorkspaceContext;
  readonly annotations: readonly AnnotationLine[];
  readonly error?: string;
}

export interface ProcessResult {
  readonly exitCode: number;
  readonly standardOutput: string;
  readonly standardError: string;
}

export function localAnnotation(text: string): AnnotationLine {
  return {
    change: 0,
    user: '',
    client: '',
    text,
    isLocal: true
  };
}
